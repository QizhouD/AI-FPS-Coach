"""On-disk persistence for completed vision sessions.

The job manager keeps results in memory only, so before this existed a restart
erased every analysed round. Comparing one practice round against the next, and
accumulating runs for offline evaluation, both need the results to outlive the
process.

Deliberately files rather than a database: a session is written once, read
whole, and never queried across fields, so JSON on disk carries none of the
operational weight a database would add.
"""

from __future__ import annotations

import json
import shutil
from pathlib import Path
from threading import Lock

from .vision.schemas import SessionMetrics, SessionSummary, VisionFrameResponse

METRICS_FILE = "metrics.json"
FRAMES_FILE = "frames.json"


class SessionStore:
    def __init__(self, root: str | Path) -> None:
        self.root = Path(root).expanduser().resolve()
        self.sessions_root = self.root / "sessions"
        self._lock = Lock()

    def _session_dir(self, job_id: str) -> Path:
        # Job ids are generated as uuid4 hex, but this is a filesystem path so
        # anything arriving from a URL still has to be rejected explicitly.
        if not job_id or not job_id.replace("-", "").replace("_", "").isalnum():
            raise ValueError("invalid job id")
        return self.sessions_root / job_id

    def save(
        self,
        metrics: SessionMetrics,
        frames: list[VisionFrameResponse],
    ) -> Path:
        """Persist one session.

        Metrics and frames go to separate files so that listing sessions stays
        cheap: a round of frames runs to megabytes, the metrics to a few
        kilobytes, and the index only ever needs the latter.
        """
        job_id = metrics.job_id or metrics.session_id
        directory = self._session_dir(job_id)
        with self._lock:
            directory.mkdir(parents=True, exist_ok=True)
            _write_json(directory / METRICS_FILE, metrics.model_dump(mode="json"))
            _write_json(
                directory / FRAMES_FILE,
                [frame.model_dump(mode="json") for frame in frames],
            )
        return directory

    def load_metrics(self, job_id: str) -> SessionMetrics | None:
        path = self._session_dir(job_id) / METRICS_FILE
        payload = _read_json(path)
        if payload is None:
            return None
        return SessionMetrics.model_validate(payload)

    def load_frames(self, job_id: str) -> list[VisionFrameResponse] | None:
        path = self._session_dir(job_id) / FRAMES_FILE
        payload = _read_json(path)
        if payload is None:
            return None
        return [VisionFrameResponse.model_validate(item) for item in payload]

    def list_sessions(self) -> list[SessionSummary]:
        if not self.sessions_root.is_dir():
            return []
        summaries: list[SessionSummary] = []
        for directory in self.sessions_root.iterdir():
            if not directory.is_dir():
                continue
            payload = _read_json(directory / METRICS_FILE)
            if payload is None:
                continue
            try:
                metrics = SessionMetrics.model_validate(payload)
            except Exception:
                # A half-written or outdated session must not break the listing.
                continue
            summaries.append(
                SessionSummary(
                    job_id=metrics.job_id or directory.name,
                    session_id=metrics.session_id,
                    created_at=metrics.created_at,
                    video_name=metrics.video_name,
                    duration_seconds=metrics.duration_seconds,
                    sampled_frames=metrics.sampled_frames,
                    mean_deviation_deg=metrics.placement_deviation.mean_deg,
                    vertical_bias_deg=metrics.vertical_bias.mean_deg,
                    on_target_ratio=metrics.effective_tracking.on_target_ratio,
                    detected_shots=metrics.shots.detected_shots,
                )
            )
        summaries.sort(key=lambda item: item.created_at, reverse=True)
        return summaries

    def delete(self, job_id: str) -> bool:
        directory = self._session_dir(job_id)
        if not directory.is_dir():
            return False
        with self._lock:
            shutil.rmtree(directory, ignore_errors=True)
        return True


def _write_json(path: Path, payload: object) -> None:
    """Write via a temporary file so a crash cannot leave a truncated session."""
    temporary = path.with_suffix(path.suffix + ".tmp")
    with temporary.open("w", encoding="utf-8") as handle:
        json.dump(payload, handle, ensure_ascii=False)
    temporary.replace(path)


def _read_json(path: Path) -> object | None:
    if not path.is_file():
        return None
    try:
        with path.open("r", encoding="utf-8") as handle:
            return json.load(handle)
    except (OSError, json.JSONDecodeError):
        return None
