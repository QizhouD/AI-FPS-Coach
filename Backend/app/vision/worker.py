from __future__ import annotations

from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass, field
from pathlib import Path
from threading import Lock
from time import monotonic, perf_counter
from uuid import uuid4

import numpy as np

from .aim_selector import AimSmoother, select_recommended_aim
from .crosshair_detector import CrosshairDetector
from .detector import UltralyticsDetector
from .geometry import CS2_DEFAULT_FOV_DEG, camera_from_frame
from .metrics import compute_session_metrics
from .schemas import (
    ActualCrosshair,
    RecommendedAim,
    SessionMetrics,
    ShotStats,
    VisionEnemy,
    VisionFrameResponse,
    VisionJobResponse,
)
from .shot_detector import analyze_shots

# Completed jobs stay addressable in memory for this long. Results are written
# to the session store first, so eviction costs nothing but a slower re-read.
DEFAULT_JOB_TTL_SECONDS = 3600.0


@dataclass
class VisionInferenceEngine:
    enemy_model_path: str | None = None
    crosshair_model_path: str | None = None
    crosshair_baseline: bool = True
    confidence: float = 0.25
    device: str = "cpu"
    fov_deg: float = CS2_DEFAULT_FOV_DEG
    # None infers at the frame's native long side. Downscaling is what makes
    # heads undetectable, so this is deliberately not a fixed 640.
    image_size: int | None = None
    _smoothers: dict[str, AimSmoother] = field(default_factory=dict)
    _lock: Lock = field(default_factory=Lock)

    def __post_init__(self) -> None:
        self.enemy_detector = UltralyticsDetector(
            self.enemy_model_path,
            self.confidence,
            self.device,
            image_size=self.image_size,
        )
        self.crosshair_detector = CrosshairDetector(
            self.crosshair_model_path,
            self.confidence,
            self.device,
            use_center_baseline=self.crosshair_baseline,
        )

    def reset_session(self, session_id: str) -> None:
        """Drop a session's smoothing state.

        Without this the smoother dictionary grows for the lifetime of the
        process, and a re-analysed session would start from the previous run's
        last aim point.
        """
        with self._lock:
            self._smoothers.pop(session_id, None)

    def analyze_image(
        self,
        image: np.ndarray,
        timestamp: float = 0.0,
        frame_index: int = 0,
        session_id: str = "anonymous",
        fov_deg: float | None = None,
    ) -> VisionFrameResponse:
        started = perf_counter()
        height, width = image.shape[:2]
        camera = camera_from_frame(
            width,
            height,
            self.fov_deg if fov_deg is None else fov_deg,
        )
        with self._lock:
            enemy_result = self.enemy_detector.detect(image)
            crosshair = self.crosshair_detector.detect(image)
            smoother = self._smoothers.setdefault(session_id, AimSmoother())
            reference_point = (
                float(crosshair["x"]),
                float(crosshair["y"]),
            ) if crosshair["visible"] else (0.5, 0.5)
            aim = select_recommended_aim(
                enemy_result.detections,
                reference_point,
                smoother,
                camera,
            )

        enemies = [
            VisionEnemy(
                id=detection.identifier,
                team=detection.team,
                part=detection.part,
                x1=detection.x1,
                y1=detection.y1,
                x2=detection.x2,
                y2=detection.y2,
                confidence=detection.confidence,
            )
            for detection in enemy_result.detections
        ]
        return VisionFrameResponse(
            timestamp=timestamp,
            frame_index=frame_index,
            frame_width=width,
            frame_height=height,
            actual_crosshair=ActualCrosshair(**crosshair),
            enemies=enemies,
            recommended_aim=RecommendedAim(**aim),
            inference_ms=(perf_counter() - started) * 1000.0,
            diagnostics={
                "enemy_model": enemy_result.message,
                "crosshair_model": self.crosshair_detector.status,
            },
        )


@dataclass
class _VisionJob:
    job_id: str
    session_id: str = "anonymous"
    video_name: str | None = None
    status: str = "queued"
    progress: float = 0.0
    processed_frames: int = 0
    total_frames: int = 0
    error: str | None = None
    results: list[VisionFrameResponse] = field(default_factory=list)
    metrics: SessionMetrics | None = None
    finished_at: float | None = None


class VisionJobManager:
    def __init__(
        self,
        engine: VisionInferenceEngine,
        media_root: str,
        store: object | None = None,
        max_workers: int = 1,
        job_ttl_seconds: float = DEFAULT_JOB_TTL_SECONDS,
    ) -> None:
        self.engine = engine
        self.media_root = Path(media_root).resolve()
        self.store = store
        self.job_ttl_seconds = job_ttl_seconds
        self._jobs: dict[str, _VisionJob] = {}
        self._lock = Lock()
        # Serialised by default on purpose: inference runs on one GPU, so
        # overlapping jobs would contend for VRAM and finish no sooner.
        self._executor = ThreadPoolExecutor(max_workers=max(1, max_workers))

    def validate_path(self, requested_path: str) -> Path:
        candidate = Path(requested_path).expanduser().resolve()
        try:
            candidate.relative_to(self.media_root)
        except ValueError as exc:
            raise ValueError("video_path must be inside the configured media root") from exc
        if not candidate.is_file():
            raise ValueError("video file does not exist")
        return candidate

    def submit(
        self,
        video_path: str,
        session_id: str,
        sample_rate: float,
        fov_deg: float | None = None,
        tracking_threshold_deg: float = 5.0,
        detect_shots: bool = True,
    ) -> str:
        candidate = self.validate_path(video_path)
        job_id = uuid4().hex
        job = _VisionJob(
            job_id=job_id,
            session_id=session_id,
            video_name=candidate.name,
        )
        self._evict_expired()
        with self._lock:
            self._jobs[job_id] = job
        self.engine.reset_session(session_id)
        self._executor.submit(
            self._run,
            job,
            candidate,
            session_id,
            sample_rate,
            fov_deg,
            tracking_threshold_deg,
            detect_shots,
        )
        return job_id

    def get(
        self,
        job_id: str,
        results_from: int | None = None,
        limit: int | None = None,
    ) -> VisionJobResponse | None:
        """Read job state.

        ``results_from`` opts into progressive delivery: frames analysed so far
        are returned while the job is still running, so a long recording shows
        overlays from the beginning instead of nothing until the end. Without
        it, results appear only on completion, which is what the existing client
        expects.
        """
        self._evict_expired()
        with self._lock:
            job = self._jobs.get(job_id)
            if job is None:
                return None

            if results_from is None:
                results = list(job.results) if job.status == "completed" else None
            else:
                start = max(0, results_from)
                window = job.results[start:]
                if limit is not None and limit >= 0:
                    window = window[:limit]
                results = list(window)

            return VisionJobResponse(
                job_id=job.job_id,
                status=job.status,
                progress=job.progress,
                processed_frames=job.processed_frames,
                total_frames=job.total_frames,
                error=job.error,
                results=results,
                metrics=job.metrics,
            )

    def shutdown(self, wait: bool = True) -> None:
        """Stop accepting work and let running jobs finish.

        A job holds the video file open for as long as it runs, so anything that
        needs to move or delete the recording afterwards has to wait for this.
        """
        self._executor.shutdown(wait=wait)

    def _evict_expired(self) -> None:
        if self.job_ttl_seconds <= 0.0:
            return
        cutoff = monotonic() - self.job_ttl_seconds
        with self._lock:
            expired = [
                job_id
                for job_id, job in self._jobs.items()
                if job.finished_at is not None and job.finished_at < cutoff
            ]
            for job_id in expired:
                self._jobs.pop(job_id, None)

    def _run(
        self,
        job: _VisionJob,
        video_path: Path,
        session_id: str,
        sample_rate: float,
        fov_deg: float | None,
        tracking_threshold_deg: float,
        detect_shots: bool,
    ) -> None:
        try:
            import cv2
        except ImportError:
            self._fail(job, "opencv-python is required for video jobs")
            return

        capture = cv2.VideoCapture(str(video_path))
        if not capture.isOpened():
            self._fail(job, "unable to open video")
            return
        fps = capture.get(cv2.CAP_PROP_FPS) or 30.0
        total_frames = int(capture.get(cv2.CAP_PROP_FRAME_COUNT) or 0)
        step = max(1, round(fps / sample_rate))
        job.total_frames = total_frames
        job.status = "running"
        frame_index = 0
        try:
            while True:
                success, frame = capture.read()
                if not success:
                    break
                if frame_index % step == 0:
                    result = self.engine.analyze_image(
                        frame,
                        timestamp=frame_index / fps,
                        frame_index=frame_index,
                        session_id=session_id,
                        fov_deg=fov_deg,
                    )
                    job.results.append(result)
                    job.processed_frames += 1
                frame_index += 1
                job.progress = frame_index / total_frames if total_frames else 0.0
        except Exception as exc:
            self._fail(job, str(exc))
            return
        finally:
            capture.release()

        try:
            job.status = "summarizing"
            shots = (
                analyze_shots(video_path, job.results)
                if detect_shots
                else ShotStats(source="disabled", message="shot detection was disabled")
            )
            job.metrics = compute_session_metrics(
                job.results,
                session_id=session_id,
                job_id=job.job_id,
                video_name=job.video_name,
                fov_deg=(
                    self.engine.fov_deg if fov_deg is None else fov_deg
                ),
                sample_rate=sample_rate,
                tracking_threshold_deg=tracking_threshold_deg,
                shots=shots,
            )
            self._persist(job)
        except Exception as exc:
            # The frames are already analysed, so a summarising failure must not
            # discard them. Surface it in diagnostics and still complete.
            job.error = f"metrics unavailable: {exc}"

        job.status = "completed"
        job.progress = 1.0
        job.finished_at = monotonic()

    def _persist(self, job: _VisionJob) -> None:
        if self.store is None or job.metrics is None:
            return
        try:
            self.store.save(job.metrics, job.results)
        except Exception as exc:  # pragma: no cover - depends on the filesystem
            job.error = f"session was not saved: {exc}"

    def _fail(self, job: _VisionJob, message: str) -> None:
        job.status = "failed"
        job.error = message
        job.finished_at = monotonic()
