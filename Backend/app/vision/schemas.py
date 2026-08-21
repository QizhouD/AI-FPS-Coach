from __future__ import annotations

from pydantic import BaseModel, Field


class ActualCrosshair(BaseModel):
    x: float = 0.5
    y: float = 0.5
    confidence: float = 0.0
    visible: bool = False
    source: str = "none"


class VisionEnemy(BaseModel):
    id: str
    team: str
    part: str
    x1: float
    y1: float
    x2: float
    y2: float
    confidence: float


class RecommendedAim(BaseModel):
    x: float = 0.5
    y: float = 0.5
    target_id: str | None = None
    confidence: float = 0.0
    offset_x: float = 0.0
    offset_y: float = 0.0


class VisionFrameResponse(BaseModel):
    timestamp: float
    frame_index: int
    frame_width: int
    frame_height: int
    actual_crosshair: ActualCrosshair
    enemies: list[VisionEnemy] = Field(default_factory=list)
    recommended_aim: RecommendedAim
    inference_ms: float
    diagnostics: dict[str, str] = Field(default_factory=dict)


class VisionVideoRequest(BaseModel):
    video_path: str
    session_id: str = "anonymous"
    sample_rate: float = Field(default=5.0, gt=0.0, le=30.0)


class VisionJobResponse(BaseModel):
    job_id: str
    status: str
    progress: float = 0.0
    processed_frames: int = 0
    total_frames: int = 0
    error: str | None = None
    results: list[VisionFrameResponse] | None = None
