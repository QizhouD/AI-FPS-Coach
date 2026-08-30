from __future__ import annotations

import os
import tempfile
from datetime import datetime, timezone
from pathlib import Path

from fastapi import FastAPI, File, Form, HTTPException, Query, UploadFile
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
from starlette.concurrency import run_in_threadpool

from .demo_analyzer import analyze_cs2_demo, sample_analysis
from .store import SessionStore
from .vision import detector, shot_detector
from .vision.geometry import CS2_DEFAULT_FOV_DEG
from .vision.schemas import (
    RecordingEntry,
    SessionMetrics,
    SessionSummary,
    VisionFrameResponse,
    VisionJobResponse,
    VisionVideoRequest,
)
from .vision.worker import VisionInferenceEngine, VisionJobManager


app = FastAPI(
    title="FPS AI Coach API",
    version="0.2.0",
    description=(
        "Offline practice review: video in, per-frame detections and "
        "session-level aim metrics out."
    ),
)
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)


def _optional_path(name: str) -> str | None:
    value = os.getenv(name, "").strip()
    return value or None


def _env_bool(name: str, default: bool) -> bool:
    value = os.getenv(name)
    if value is None:
        return default
    return value.strip().lower() in {"1", "true", "yes", "on"}


def _optional_int(name: str) -> int | None:
    value = os.getenv(name, "").strip()
    if not value:
        return None
    try:
        return int(value)
    except ValueError:
        return None


vision_engine = VisionInferenceEngine(
    enemy_model_path=_optional_path("FPS_VISION_ENEMY_MODEL_PATH"),
    crosshair_model_path=_optional_path("FPS_VISION_CROSSHAIR_MODEL_PATH"),
    crosshair_baseline=_env_bool("FPS_VISION_CROSSHAIR_BASELINE", True),
    confidence=float(os.getenv("FPS_VISION_CONFIDENCE", "0.25")),
    device=os.getenv("FPS_VISION_DEVICE", "cpu"),
    fov_deg=float(os.getenv("FPS_VISION_FOV_DEG", str(CS2_DEFAULT_FOV_DEG))),
    image_size=_optional_int("FPS_VISION_IMGSZ"),
)
session_store = SessionStore(
    os.getenv("FPS_VISION_DATA_ROOT", str(Path.cwd() / "data"))
)
vision_jobs = VisionJobManager(
    vision_engine,
    os.getenv("FPS_VISION_MEDIA_ROOT", str(Path.cwd() / "media")),
    store=session_store,
    job_ttl_seconds=float(os.getenv("FPS_VISION_JOB_TTL_SECONDS", "3600")),
)


class DemoPlayerStats(BaseModel):
    name: str
    kills: int
    deaths: int
    assists: int
    headshots: int
    headshot_percentage: float
    kd_ratio: float
    damage: int
    adr: float
    opening_kills: int
    opening_deaths: int


class DemoInsight(BaseModel):
    severity: str
    title: str
    evidence: str
    action: str


class DemoPlaybackBounds(BaseModel):
    min_x: float
    max_x: float
    min_y: float
    max_y: float


class DemoPlaybackPlayer(BaseModel):
    id: str
    name: str
    team: int
    x: float
    y: float
    health: int
    alive: bool
    yaw: float


class DemoPlaybackFrame(BaseModel):
    tick: int
    time: float
    round: int
    players: list[DemoPlaybackPlayer]


class DemoPlayback(BaseModel):
    duration: float
    tick_rate: float
    sample_rate: float
    coordinate_space: str
    bounds: DemoPlaybackBounds
    frames: list[DemoPlaybackFrame]


class DemoAnalysisResponse(BaseModel):
    analysis_id: str
    file_name: str
    map_name: str
    rounds: int
    data_source: str
    player: DemoPlayerStats
    insights: list[DemoInsight]
    playback: DemoPlayback


def _cuda_status() -> tuple[bool, str | None]:
    try:
        import torch
    except ImportError:
        return False, None
    if not torch.cuda.is_available():
        return False, None
    try:
        return True, torch.cuda.get_device_name(0)
    except Exception:
        return True, None


@app.get("/health")
def health() -> dict:
    cuda_available, cuda_name = _cuda_status()
    shot_detection = shot_detector.find_ffmpeg()
    return {
        "status": "ok",
        "service": "fps-ai-coach",
        "vision": {
            "device": vision_engine.device,
            "cuda_available": cuda_available,
            "cuda_name": cuda_name,
            "enemy_model": vision_engine.enemy_detector.status,
            "crosshair_model": vision_engine.crosshair_detector.status,
            "crosshair_baseline": vision_engine.crosshair_baseline,
            "fov_deg": vision_engine.fov_deg,
            # Reported because downscaling silently destroys head detections,
            # so a wrong value here looks like a bad model rather than a setting.
            "image_size": vision_engine.enemy_detector.image_size or "native",
            "image_size_at_1080p": detector.image_size_for(
                1920, 1080, vision_engine.enemy_detector.image_size
            ),
            "media_root": str(vision_jobs.media_root),
            "data_root": str(session_store.root),
            "shot_detection": "ready" if shot_detection else "ffmpeg not available",
        },
    }


@app.post("/api/v1/vision/frame", response_model=VisionFrameResponse)
async def analyze_vision_frame(
    frame: UploadFile = File(...),
    timestamp: float = Form(0.0),
    frame_index: int = Form(0),
    session_id: str = Form("anonymous"),
) -> VisionFrameResponse:
    payload = await frame.read()
    try:
        import cv2
        import numpy as np
    except ImportError as exc:
        raise HTTPException(
            status_code=503,
            detail="Vision dependencies are not installed: opencv-python and numpy.",
        ) from exc

    image = cv2.imdecode(np.frombuffer(payload, dtype=np.uint8), cv2.IMREAD_COLOR)
    if image is None:
        raise HTTPException(status_code=400, detail="Unable to decode the image frame.")
    return vision_engine.analyze_image(
        image,
        timestamp=timestamp,
        frame_index=frame_index,
        session_id=session_id,
    )


@app.post("/api/v1/vision/video", response_model=VisionJobResponse)
def submit_vision_video(request: VisionVideoRequest) -> VisionJobResponse:
    try:
        job_id = vision_jobs.submit(
            request.video_path,
            request.session_id,
            request.sample_rate,
            fov_deg=request.fov_deg,
            tracking_threshold_deg=request.tracking_threshold_deg,
            detect_shots=request.detect_shots,
        )
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
    result = vision_jobs.get(job_id)
    if result is None:
        raise HTTPException(status_code=500, detail="Unable to create vision job.")
    return result


@app.get("/api/v1/vision/jobs/{job_id}", response_model=VisionJobResponse)
def get_vision_job(
    job_id: str,
    results_from: int | None = Query(
        default=None,
        ge=0,
        description=(
            "Return frames analysed so far starting at this index, instead of "
            "waiting for the job to complete."
        ),
    ),
    limit: int | None = Query(default=None, ge=0, le=20000),
) -> VisionJobResponse:
    result = vision_jobs.get(job_id, results_from=results_from, limit=limit)
    if result is None:
        raise HTTPException(status_code=404, detail="Vision job not found.")
    return result


VIDEO_SUFFIXES = frozenset({".mp4", ".mkv", ".mov", ".flv", ".avi", ".webm"})


@app.get("/api/v1/vision/recordings", response_model=list[RecordingEntry])
def list_recordings(limit: int = Query(default=25, ge=1, le=200)) -> list[RecordingEntry]:
    """Newest recordings in the media root, so a finished practice run is one click away.

    Polled rather than watched: OBS writes the file over the length of the round and only
    finalises it on stop, so an inotify-style event would fire long before the video is
    playable. Listing on demand also survives the backend having been restarted mid-session.
    """
    root = vision_jobs.media_root
    if not root.is_dir():
        return []

    analyzed = {
        summary.video_name
        for summary in session_store.list_sessions()
        if summary.video_name
    }

    found: list[RecordingEntry] = []
    for path in root.rglob("*"):
        if not path.is_file() or path.suffix.lower() not in VIDEO_SUFFIXES:
            continue
        try:
            stat = path.stat()
        except OSError:
            continue
        found.append(
            RecordingEntry(
                name=path.name,
                path=str(path),
                size_bytes=stat.st_size,
                modified_at=datetime.fromtimestamp(
                    stat.st_mtime,
                    tz=timezone.utc,
                ).isoformat(),
                analyzed=path.name in analyzed,
            )
        )

    found.sort(key=lambda entry: entry.modified_at, reverse=True)
    return found[:limit]


@app.get("/api/v1/vision/sessions", response_model=list[SessionSummary])
def list_vision_sessions() -> list[SessionSummary]:
    return session_store.list_sessions()


@app.get(
    "/api/v1/vision/sessions/{job_id}/metrics",
    response_model=SessionMetrics,
)
def get_vision_session_metrics(job_id: str) -> SessionMetrics:
    try:
        metrics = session_store.load_metrics(job_id)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
    if metrics is None:
        raise HTTPException(status_code=404, detail="Session not found.")
    return metrics


@app.get(
    "/api/v1/vision/sessions/{job_id}/frames",
    response_model=list[VisionFrameResponse],
)
def get_vision_session_frames(job_id: str) -> list[VisionFrameResponse]:
    try:
        frames = session_store.load_frames(job_id)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
    if frames is None:
        raise HTTPException(status_code=404, detail="Session not found.")
    return frames


@app.delete("/api/v1/vision/sessions/{job_id}")
def delete_vision_session(job_id: str) -> dict:
    try:
        removed = session_store.delete(job_id)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
    if not removed:
        raise HTTPException(status_code=404, detail="Session not found.")
    return {"status": "deleted", "job_id": job_id}


@app.get("/api/v1/analyze/demo/sample", response_model=DemoAnalysisResponse)
def analyze_demo_sample() -> dict:
    return sample_analysis()


@app.post("/api/v1/analyze/demo", response_model=DemoAnalysisResponse)
async def analyze_demo(
    demo: UploadFile = File(...),
    target_player: str = Form(""),
) -> dict:
    original_name = demo.filename or "match.dem"
    if not original_name.lower().endswith(".dem"):
        raise HTTPException(status_code=400, detail="Only CS2 .dem files are supported.")

    file_descriptor, temp_path = tempfile.mkstemp(suffix=".dem")
    os.close(file_descriptor)
    total_bytes = 0
    max_bytes = 1_500 * 1024 * 1024
    try:
        with open(temp_path, "wb") as output:
            while chunk := await demo.read(1024 * 1024):
                total_bytes += len(chunk)
                if total_bytes > max_bytes:
                    raise HTTPException(status_code=413, detail="Demo files cannot exceed 1.5 GB.")
                output.write(chunk)

        try:
            return await run_in_threadpool(
                analyze_cs2_demo,
                temp_path,
                original_name,
                target_player,
            )
        except ValueError as exc:
            raise HTTPException(status_code=422, detail=str(exc)) from exc
    finally:
        try:
            os.remove(temp_path)
        except FileNotFoundError:
            pass
