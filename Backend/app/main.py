from __future__ import annotations

from datetime import datetime, timezone
from hashlib import sha256
import os
import tempfile
from pathlib import Path

from fastapi import FastAPI, File, Form, HTTPException, UploadFile
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
from starlette.concurrency import run_in_threadpool

from .demo_analyzer import analyze_cs2_demo, sample_analysis
from .vision.schemas import VisionFrameResponse, VisionJobResponse, VisionVideoRequest
from .vision.worker import VisionInferenceEngine, VisionJobManager


app = FastAPI(
    title="FPS AI Coach Live API",
    version="0.1.0",
    description="MVP frame-analysis contract. Replace the heuristic analyzer with CV/LLM workers.",
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


vision_engine = VisionInferenceEngine(
    enemy_model_path=_optional_path("FPS_VISION_ENEMY_MODEL_PATH"),
    crosshair_model_path=_optional_path("FPS_VISION_CROSSHAIR_MODEL_PATH"),
    crosshair_baseline=_env_bool("FPS_VISION_CROSSHAIR_BASELINE", True),
    confidence=float(os.getenv("FPS_VISION_CONFIDENCE", "0.25")),
    device=os.getenv("FPS_VISION_DEVICE", "cpu"),
)
vision_jobs = VisionJobManager(
    vision_engine,
    os.getenv("FPS_VISION_MEDIA_ROOT", str(Path.cwd() / "media")),
)


class Scores(BaseModel):
    aim: int
    positioning: int
    decision: int
    consistency: int


class Tip(BaseModel):
    severity: str
    title: str
    message: str
    action: str


class AnalysisResponse(BaseModel):
    session_id: str
    timestamp: str
    scores: Scores
    tip: Tip


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


TIPS = (
    Tip(
        severity="good",
        title="Stable pre-aim pacing",
        message="Recent camera movement is stable enough for information gathering.",
        action="Keep the crosshair at likely head level and measure time to first shot.",
    ),
    Tip(
        severity="warning",
        title="Reduce movement without information",
        message="Frequent camera changes can hide important audio and minimap information.",
        action="Pause before rotating and confirm teammates, economy, and remaining time.",
    ),
    Tip(
        severity="danger",
        title="Avoid repeated forced duels",
        message="The current pacing is aggressive and repeated exposure has low value without trade support.",
        action="Return to cover and wait for crossfire support before re-peeking.",
    ),
)


@app.get("/health")
def health() -> dict[str, str]:
    return {"status": "ok", "service": "fps-ai-coach-live"}


@app.post("/api/v1/analyze/frame", response_model=AnalysisResponse)
async def analyze_frame(
    frame: UploadFile = File(...),
    game: str = Form("auto"),
    session_id: str = Form("anonymous"),
) -> AnalysisResponse:
    payload = await frame.read()
    digest = sha256(payload).digest()
    tip = TIPS[digest[0] % len(TIPS)]
    base = 62 + digest[1] % 17
    game_adjustment = 2 if game.lower() in {"cs2", "valorant"} else 0

    return AnalysisResponse(
        session_id=session_id,
        timestamp=datetime.now(timezone.utc).isoformat(),
        scores=Scores(
            aim=min(99, base + game_adjustment),
            positioning=60 + digest[2] % 21,
            decision=61 + digest[3] % 20,
            consistency=58 + digest[4] % 22,
        ),
        tip=tip,
    )


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
        )
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
    result = vision_jobs.get(job_id)
    if result is None:
        raise HTTPException(status_code=500, detail="Unable to create vision job.")
    return result


@app.get("/api/v1/vision/jobs/{job_id}", response_model=VisionJobResponse)
def get_vision_job(job_id: str) -> VisionJobResponse:
    result = vision_jobs.get(job_id)
    if result is None:
        raise HTTPException(status_code=404, detail="Vision job not found.")
    return result


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
