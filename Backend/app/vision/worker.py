from __future__ import annotations

from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass, field
from pathlib import Path
from threading import Lock
from time import perf_counter
from uuid import uuid4

import numpy as np

from .aim_selector import AimSmoother, select_recommended_aim
from .crosshair_detector import CrosshairDetector
from .detector import UltralyticsDetector
from .schemas import (
    ActualCrosshair,
    RecommendedAim,
    VisionEnemy,
    VisionFrameResponse,
    VisionJobResponse,
)


@dataclass
class VisionInferenceEngine:
    enemy_model_path: str | None = None
    crosshair_model_path: str | None = None
    crosshair_baseline: bool = True
    confidence: float = 0.25
    device: str = "cpu"
    _smoothers: dict[str, AimSmoother] = field(default_factory=dict)
    _lock: Lock = field(default_factory=Lock)

    def __post_init__(self) -> None:
        self.enemy_detector = UltralyticsDetector(
            self.enemy_model_path,
            self.confidence,
            self.device,
        )
        self.crosshair_detector = CrosshairDetector(
            self.crosshair_model_path,
            self.confidence,
            self.device,
            use_center_baseline=self.crosshair_baseline,
        )

    def analyze_image(
        self,
        image: np.ndarray,
        timestamp: float = 0.0,
        frame_index: int = 0,
        session_id: str = "anonymous",
    ) -> VisionFrameResponse:
        started = perf_counter()
        height, width = image.shape[:2]
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
    status: str = "queued"
    progress: float = 0.0
    processed_frames: int = 0
    total_frames: int = 0
    error: str | None = None
    results: list[VisionFrameResponse] = field(default_factory=list)


class VisionJobManager:
    def __init__(self, engine: VisionInferenceEngine, media_root: str) -> None:
        self.engine = engine
        self.media_root = Path(media_root).resolve()
        self._jobs: dict[str, _VisionJob] = {}
        self._lock = Lock()
        self._executor = ThreadPoolExecutor(max_workers=1)

    def validate_path(self, requested_path: str) -> Path:
        candidate = Path(requested_path).expanduser().resolve()
        try:
            candidate.relative_to(self.media_root)
        except ValueError as exc:
            raise ValueError("video_path must be inside the configured media root") from exc
        if not candidate.is_file():
            raise ValueError("video file does not exist")
        return candidate

    def submit(self, video_path: str, session_id: str, sample_rate: float) -> str:
        candidate = self.validate_path(video_path)
        job_id = uuid4().hex
        job = _VisionJob(job_id=job_id)
        with self._lock:
            self._jobs[job_id] = job
        self._executor.submit(self._run, job, candidate, session_id, sample_rate)
        return job_id

    def get(self, job_id: str) -> VisionJobResponse | None:
        with self._lock:
            job = self._jobs.get(job_id)
            if job is None:
                return None
            results = job.results if job.status == "completed" else None
            return VisionJobResponse(
                job_id=job.job_id,
                status=job.status,
                progress=job.progress,
                processed_frames=job.processed_frames,
                total_frames=job.total_frames,
                error=job.error,
                results=results,
            )

    def _run(
        self,
        job: _VisionJob,
        video_path: Path,
        session_id: str,
        sample_rate: float,
    ) -> None:
        try:
            import cv2
        except ImportError as exc:
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
                    )
                    job.results.append(result)
                    job.processed_frames += 1
                frame_index += 1
                job.progress = frame_index / total_frames if total_frames else 0.0
            job.status = "completed"
            job.progress = 1.0
        except Exception as exc:
            self._fail(job, str(exc))
        finally:
            capture.release()

    def _fail(self, job: _VisionJob, message: str) -> None:
        job.status = "failed"
        job.error = message
