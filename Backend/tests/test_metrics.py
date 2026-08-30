from __future__ import annotations

import tempfile
import unittest

from app.store import SessionStore
from app.vision.geometry import CameraModel
from app.vision.metrics import compute_session_metrics
from app.vision.schemas import (
    ActualCrosshair,
    RecommendedAim,
    VisionFrameResponse,
)


def make_frame(
    timestamp: float,
    *,
    target: tuple[float, float] | None,
    camera: CameraModel,
    index: int = 0,
) -> VisionFrameResponse:
    """A frame whose aim fields are derived the same way the engine derives them."""
    reference = (0.5, 0.5)
    if target is None:
        aim = RecommendedAim()
    else:
        horizontal, vertical = camera.axis_angles_deg(target, reference)
        aim = RecommendedAim(
            x=target[0],
            y=target[1],
            target_id="head",
            confidence=0.9,
            offset_x=target[0] - reference[0],
            offset_y=target[1] - reference[1],
            offset_deg_x=horizontal,
            offset_deg_y=vertical,
            offset_deg=camera.angular_distance_deg(target, reference),
        )
    return VisionFrameResponse(
        timestamp=timestamp,
        frame_index=index,
        frame_width=camera.width,
        frame_height=camera.height,
        actual_crosshair=ActualCrosshair(visible=True, source="screen_center_baseline"),
        enemies=[],
        recommended_aim=aim,
        inference_ms=9.0,
    )


class MetricsTests(unittest.TestCase):
    def setUp(self) -> None:
        self.camera = CameraModel(width=1920, height=1080)

    def test_frames_without_a_target_are_excluded_not_counted_as_zero(self) -> None:
        """Including empty frames as zero deviation would flatter every average."""
        frames = [
            make_frame(0.0, target=(0.5, 0.4), camera=self.camera, index=0),
            make_frame(0.1, target=None, camera=self.camera, index=1),
            make_frame(0.2, target=None, camera=self.camera, index=2),
            make_frame(0.3, target=(0.5, 0.4), camera=self.camera, index=3),
        ]

        metrics = compute_session_metrics(frames, fov_deg=90.0, sample_rate=10.0)

        self.assertEqual(metrics.sampled_frames, 4)
        self.assertEqual(metrics.frames_with_target, 2)
        self.assertAlmostEqual(metrics.target_visibility_ratio, 0.5)
        # Both measured frames sit at the same deviation, so the mean equals it.
        single = self.camera.angular_distance_deg((0.5, 0.4), (0.5, 0.5))
        self.assertAlmostEqual(metrics.placement_deviation.mean_deg, single, places=6)

    def test_consistent_low_aim_is_reported_as_such(self) -> None:
        # Heads sit above the crosshair in every frame, meaning the player aims low.
        frames = [
            make_frame(index * 0.1, target=(0.5, 0.42), camera=self.camera, index=index)
            for index in range(10)
        ]

        metrics = compute_session_metrics(frames, fov_deg=90.0, sample_rate=10.0)

        self.assertEqual(metrics.vertical_bias.direction, "aims_low")
        self.assertGreater(metrics.vertical_bias.mean_deg, 0.0)
        self.assertAlmostEqual(metrics.vertical_bias.positive_ratio, 1.0)
        self.assertIn("below head level", metrics.headline)

    def test_symmetric_error_reports_no_bias_despite_large_deviation(self) -> None:
        """A wide but even spread is a different fault from a consistent lean."""
        frames = []
        for index in range(10):
            offset = 0.08 if index % 2 == 0 else -0.08
            frames.append(
                make_frame(
                    index * 0.1,
                    target=(0.5, 0.5 + offset),
                    camera=self.camera,
                    index=index,
                )
            )

        metrics = compute_session_metrics(frames, fov_deg=90.0, sample_rate=10.0)

        self.assertEqual(metrics.vertical_bias.direction, "neutral")
        self.assertGreater(metrics.placement_deviation.mean_deg, 1.0)
        self.assertAlmostEqual(metrics.vertical_bias.positive_ratio, 0.5)

    def test_effective_tracking_counts_only_frames_within_the_threshold(self) -> None:
        close = (0.5, 0.49)
        far = (0.9, 0.9)
        frames = [
            make_frame(0.0, target=close, camera=self.camera, index=0),
            make_frame(0.1, target=close, camera=self.camera, index=1),
            make_frame(0.2, target=far, camera=self.camera, index=2),
            make_frame(0.3, target=None, camera=self.camera, index=3),
        ]

        metrics = compute_session_metrics(
            frames,
            fov_deg=90.0,
            sample_rate=10.0,
            tracking_threshold_deg=5.0,
        )

        tracking = metrics.effective_tracking
        self.assertEqual(tracking.frames_with_target, 3)
        self.assertEqual(tracking.frames_on_target, 2)
        self.assertAlmostEqual(tracking.on_target_ratio, 2 / 3)
        self.assertAlmostEqual(tracking.on_target_seconds, 0.2, places=6)

    def test_empty_input_produces_a_valid_empty_report(self) -> None:
        metrics = compute_session_metrics([], fov_deg=90.0)
        self.assertEqual(metrics.sampled_frames, 0)
        self.assertEqual(metrics.placement_deviation.count, 0)
        self.assertIn("No frames", metrics.headline)

    def test_histogram_covers_every_measured_frame(self) -> None:
        frames = [
            make_frame(
                index * 0.1,
                target=(0.5 + index * 0.03, 0.5),
                camera=self.camera,
                index=index,
            )
            for index in range(12)
        ]

        metrics = compute_session_metrics(frames, fov_deg=90.0, sample_rate=10.0)

        total = sum(item.count for item in metrics.deviation_histogram)
        self.assertEqual(total, metrics.frames_with_target)

    def test_fov_changes_the_reported_angles(self) -> None:
        frames = [make_frame(0.0, target=(0.6, 0.5), camera=self.camera)]
        wide = compute_session_metrics(frames, fov_deg=90.0, sample_rate=10.0)

        narrow_camera = CameraModel(width=1920, height=1080, fov_deg=70.0)
        narrow_frames = [make_frame(0.0, target=(0.6, 0.5), camera=narrow_camera)]
        narrow = compute_session_metrics(narrow_frames, fov_deg=70.0, sample_rate=10.0)

        self.assertLess(
            narrow.placement_deviation.mean_deg,
            wide.placement_deviation.mean_deg,
        )


class SessionStoreTests(unittest.TestCase):
    def setUp(self) -> None:
        self.camera = CameraModel(width=1920, height=1080)
        self._directory = tempfile.TemporaryDirectory()
        self.store = SessionStore(self._directory.name)

    def tearDown(self) -> None:
        self._directory.cleanup()

    def _metrics(self, job_id: str):
        frames = [
            make_frame(index * 0.1, target=(0.5, 0.42), camera=self.camera, index=index)
            for index in range(5)
        ]
        metrics = compute_session_metrics(
            frames,
            session_id="unit-test",
            job_id=job_id,
            video_name="round.mp4",
            fov_deg=90.0,
            sample_rate=10.0,
        )
        return metrics, frames

    def test_session_survives_a_round_trip(self) -> None:
        metrics, frames = self._metrics("abc123")
        self.store.save(metrics, frames)

        loaded = self.store.load_metrics("abc123")
        self.assertIsNotNone(loaded)
        assert loaded is not None
        self.assertEqual(loaded.job_id, "abc123")
        self.assertAlmostEqual(
            loaded.placement_deviation.mean_deg,
            metrics.placement_deviation.mean_deg,
            places=6,
        )

        restored_frames = self.store.load_frames("abc123")
        self.assertIsNotNone(restored_frames)
        assert restored_frames is not None
        self.assertEqual(len(restored_frames), len(frames))

    def test_listing_is_newest_first_and_summarises(self) -> None:
        first, first_frames = self._metrics("aaa111")
        first.created_at = "2026-01-01T00:00:00+00:00"
        second, second_frames = self._metrics("bbb222")
        second.created_at = "2026-02-01T00:00:00+00:00"
        self.store.save(first, first_frames)
        self.store.save(second, second_frames)

        summaries = self.store.list_sessions()

        self.assertEqual([item.job_id for item in summaries], ["bbb222", "aaa111"])
        self.assertEqual(summaries[0].video_name, "round.mp4")
        self.assertGreater(summaries[0].vertical_bias_deg, 0.0)

    def test_missing_session_reads_as_none(self) -> None:
        self.assertIsNone(self.store.load_metrics("doesnotexist"))
        self.assertIsNone(self.store.load_frames("doesnotexist"))
        self.assertFalse(self.store.delete("doesnotexist"))

    def test_path_traversal_in_the_job_id_is_rejected(self) -> None:
        with self.assertRaises(ValueError):
            self.store.load_metrics("../../etc/passwd")
        with self.assertRaises(ValueError):
            self.store.delete("..")

    def test_delete_removes_the_session(self) -> None:
        metrics, frames = self._metrics("ccc333")
        self.store.save(metrics, frames)
        self.assertTrue(self.store.delete("ccc333"))
        self.assertIsNone(self.store.load_metrics("ccc333"))


if __name__ == "__main__":
    unittest.main()
