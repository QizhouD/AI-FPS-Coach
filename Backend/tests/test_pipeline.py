"""End-to-end coverage of the video job pipeline over the real HTTP contract.

Runs in-process through TestClient rather than against a live server, so the
whole path from ``POST /vision/video`` to a persisted session is exercised
without binding a port.
"""

from __future__ import annotations

import subprocess
import tempfile
import time
import unittest
from pathlib import Path

import numpy as np
from fastapi.testclient import TestClient

from app.store import SessionStore
from app.vision.geometry import CameraModel
from app.vision.schemas import ActualCrosshair, RecommendedAim, VisionFrameResponse
from app.vision.shot_detector import find_ffmpeg
from app.vision.worker import VisionJobManager


class StubEngine:
    """Stands in for YOLO so the pipeline is testable without weights.

    Places a head a fixed distance above the crosshair, which gives the metrics
    layer a known answer to be checked against.
    """

    fov_deg = 90.0

    def __init__(self, target_offset: float = -0.08) -> None:
        self.target_offset = target_offset
        self.reset_calls: list[str] = []

    def reset_session(self, session_id: str) -> None:
        self.reset_calls.append(session_id)

    def analyze_image(
        self,
        image: np.ndarray,
        timestamp: float = 0.0,
        frame_index: int = 0,
        session_id: str = "anonymous",
        fov_deg: float | None = None,
    ) -> VisionFrameResponse:
        height, width = image.shape[:2]
        camera = CameraModel(width, height, fov_deg or self.fov_deg)
        target = (0.5, 0.5 + self.target_offset)
        horizontal, vertical = camera.axis_angles_deg(target, (0.5, 0.5))
        return VisionFrameResponse(
            timestamp=timestamp,
            frame_index=frame_index,
            frame_width=width,
            frame_height=height,
            actual_crosshair=ActualCrosshair(
                visible=True,
                source="screen_center_baseline",
            ),
            enemies=[],
            recommended_aim=RecommendedAim(
                x=target[0],
                y=target[1],
                target_id="head",
                confidence=0.9,
                offset_x=0.0,
                offset_y=self.target_offset,
                offset_deg_x=horizontal,
                offset_deg_y=vertical,
                offset_deg=camera.angular_distance_deg(target, (0.5, 0.5)),
            ),
            inference_ms=1.0,
        )


def render_clip(destination: Path, seconds: int = 2) -> bool:
    executable = find_ffmpeg()
    if executable is None:
        return False
    result = subprocess.run(
        [
            executable, "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi",
            "-i", f"testsrc=size=320x180:rate=30:duration={seconds}",
            "-f", "lavfi",
            "-i", f"sine=frequency=800:duration={seconds}",
            "-shortest", "-pix_fmt", "yuv420p", str(destination),
        ],
        capture_output=True,
        timeout=180,
    )
    return result.returncode == 0 and destination.is_file()


class PipelineTests(unittest.TestCase):
    def setUp(self) -> None:
        if find_ffmpeg() is None:
            self.skipTest("ffmpeg is not available")

        self._media = tempfile.TemporaryDirectory()
        self._data = tempfile.TemporaryDirectory()
        self.media_root = Path(self._media.name)
        self.video = self.media_root / "round.mp4"
        if not render_clip(self.video):
            self.skipTest("unable to render the test clip")

        self.engine = StubEngine()
        self.store = SessionStore(self._data.name)
        self.jobs = VisionJobManager(
            self.engine,
            str(self.media_root),
            store=self.store,
        )
        self.client = self._build_client()

    def tearDown(self) -> None:
        # Windows refuses to delete a file a worker still has open, so the
        # executor has to drain before the temporary media root goes away.
        self.jobs.shutdown()
        self._media.cleanup()
        self._data.cleanup()

    def _build_client(self) -> TestClient:
        from app import main

        self._saved = (main.vision_jobs, main.session_store)
        main.vision_jobs = self.jobs
        main.session_store = self.store
        self.addCleanup(self._restore)
        return TestClient(main.app)

    def _restore(self) -> None:
        from app import main

        main.vision_jobs, main.session_store = self._saved

    def _wait_for_completion(self, job_id: str, timeout: float = 120.0) -> dict:
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            response = self.client.get(f"/api/v1/vision/jobs/{job_id}")
            self.assertEqual(response.status_code, 200)
            payload = response.json()
            if payload["status"] in {"completed", "failed"}:
                return payload
            time.sleep(0.1)
        self.fail("the job did not finish in time")

    def test_video_job_produces_metrics_and_persists_the_session(self) -> None:
        response = self.client.post(
            "/api/v1/vision/video",
            json={
                "video_path": str(self.video),
                "session_id": "e2e",
                "sample_rate": 10.0,
                "fov_deg": 90.0,
            },
        )
        self.assertEqual(response.status_code, 200)
        job_id = response.json()["job_id"]

        payload = self._wait_for_completion(job_id)
        self.assertEqual(payload["status"], "completed", payload.get("error"))
        self.assertGreater(payload["processed_frames"], 0)
        self.assertIsNotNone(payload["results"])

        metrics = payload["metrics"]
        self.assertIsNotNone(metrics)
        self.assertEqual(metrics["frames_with_target"], payload["processed_frames"])
        # The stub keeps every head above the crosshair, so the session must read
        # as a consistent low aim rather than as noise.
        self.assertEqual(metrics["vertical_bias"]["direction"], "aims_low")
        self.assertGreater(metrics["vertical_bias"]["mean_deg"], 0.0)
        self.assertAlmostEqual(metrics["focal_length_px"], 120.0, places=3)

        stored = self.client.get(f"/api/v1/vision/sessions/{job_id}/metrics")
        self.assertEqual(stored.status_code, 200)
        self.assertEqual(stored.json()["job_id"], job_id)

        frames = self.client.get(f"/api/v1/vision/sessions/{job_id}/frames")
        self.assertEqual(frames.status_code, 200)
        self.assertEqual(len(frames.json()), payload["processed_frames"])

        listing = self.client.get("/api/v1/vision/sessions")
        self.assertEqual(listing.status_code, 200)
        self.assertIn(job_id, [item["job_id"] for item in listing.json()])

    def test_progressive_results_are_available_before_completion(self) -> None:
        response = self.client.post(
            "/api/v1/vision/video",
            json={
                "video_path": str(self.video),
                "session_id": "progressive",
                "sample_rate": 30.0,
                "detect_shots": False,
            },
        )
        job_id = response.json()["job_id"]

        # Without the opt-in, a running job withholds results, which is what the
        # existing client depends on.
        running = self.client.get(f"/api/v1/vision/jobs/{job_id}").json()
        if running["status"] not in {"completed"}:
            self.assertIsNone(running["results"])

        self._wait_for_completion(job_id)

        window = self.client.get(
            f"/api/v1/vision/jobs/{job_id}",
            params={"results_from": 2, "limit": 3},
        ).json()
        self.assertLessEqual(len(window["results"]), 3)
        self.assertEqual(window["results"][0]["frame_index"], 2)

    def test_a_path_outside_the_media_root_is_refused(self) -> None:
        with tempfile.TemporaryDirectory() as outside:
            stray = Path(outside) / "elsewhere.mp4"
            stray.write_bytes(b"not really a video")
            response = self.client.post(
                "/api/v1/vision/video",
                json={"video_path": str(stray), "session_id": "e2e"},
            )
        self.assertEqual(response.status_code, 400)
        self.assertIn("media root", response.json()["detail"])

    def test_a_missing_file_inside_the_root_is_refused(self) -> None:
        response = self.client.post(
            "/api/v1/vision/video",
            json={"video_path": str(self.media_root / "absent.mp4")},
        )
        self.assertEqual(response.status_code, 400)

    def test_unknown_job_and_session_report_not_found(self) -> None:
        self.assertEqual(
            self.client.get("/api/v1/vision/jobs/deadbeef").status_code,
            404,
        )
        self.assertEqual(
            self.client.get("/api/v1/vision/sessions/deadbeef/metrics").status_code,
            404,
        )

    def test_submitting_resets_the_session_smoother(self) -> None:
        """Re-analysing must not inherit the previous run's last aim point."""
        self.client.post(
            "/api/v1/vision/video",
            json={"video_path": str(self.video), "session_id": "reset-me"},
        )
        self.assertIn("reset-me", self.engine.reset_calls)

    def test_completed_jobs_are_evicted_once_they_expire(self) -> None:
        expiring = VisionJobManager(
            self.engine,
            str(self.media_root),
            store=self.store,
            job_ttl_seconds=0.01,
        )
        self.addCleanup(expiring.shutdown)
        job_id = expiring.submit(str(self.video), "ttl", 10.0, detect_shots=False)
        deadline = time.monotonic() + 60.0
        while time.monotonic() < deadline:
            state = expiring.get(job_id)
            if state is None or state.status == "completed":
                break
            time.sleep(0.1)

        time.sleep(0.2)
        # The session is on disk, so dropping it from memory loses nothing.
        self.assertIsNone(expiring.get(job_id))
        self.assertIsNotNone(self.store.load_metrics(job_id))


if __name__ == "__main__":
    unittest.main()
