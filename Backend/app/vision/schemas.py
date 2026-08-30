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
    """The head nearest the crosshair, plus the deviation to it.

    ``x``/``y`` are smoothed for overlay rendering. The ``offset_*`` fields are
    the raw measurement and are ``None`` when no player is visible, which keeps
    "no target" separable from "zero deviation" in the statistics.

    ``target_source`` is ``head`` when a head box was detected and
    ``inferred_head`` when the position came from a body box whose head was too
    small to detect, so an aggregate can be split by how it was obtained.
    """

    x: float = 0.5
    y: float = 0.5
    target_id: str | None = None
    target_source: str | None = None
    confidence: float = 0.0
    offset_x: float | None = None
    offset_y: float | None = None
    # Positive means the crosshair must move right / up to reach the target.
    offset_deg_x: float | None = None
    offset_deg_y: float | None = None
    offset_deg: float | None = None


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
    # The in-game FOV setting, horizontal at 4:3. Configurable rather than
    # hard-coded because the pixel-to-angle conversion is meaningless without
    # the value the footage was actually recorded at.
    fov_deg: float | None = Field(default=None, gt=1.0, lt=179.0)
    # Deviation below which the crosshair counts as being on target.
    tracking_threshold_deg: float = Field(default=5.0, gt=0.0, le=90.0)
    detect_shots: bool = True


class VisionJobResponse(BaseModel):
    job_id: str
    status: str
    progress: float = 0.0
    processed_frames: int = 0
    total_frames: int = 0
    error: str | None = None
    results: list[VisionFrameResponse] | None = None
    metrics: "SessionMetrics | None" = None


class DeviationStats(BaseModel):
    """Distribution of a deviation series, in degrees."""

    count: int = 0
    mean_deg: float = 0.0
    median_deg: float = 0.0
    p90_deg: float = 0.0
    std_deg: float = 0.0
    min_deg: float = 0.0
    max_deg: float = 0.0


class BiasStats(BaseModel):
    """A signed deviation series, where the sign carries the coaching meaning."""

    count: int = 0
    mean_deg: float = 0.0
    median_deg: float = 0.0
    std_deg: float = 0.0
    # Share of samples on the positive side, which separates a consistent lean
    # from a wide spread that merely averages out to one.
    positive_ratio: float = 0.0
    direction: str = "neutral"


class TrackingStats(BaseModel):
    threshold_deg: float = 5.0
    frames_on_target: int = 0
    frames_with_target: int = 0
    on_target_ratio: float = 0.0
    on_target_seconds: float = 0.0
    target_visible_seconds: float = 0.0


class HistogramBin(BaseModel):
    lower_deg: float
    upper_deg: float
    count: int


class ShotMetric(BaseModel):
    """One detected shot, aligned to the nearest analysed frame."""

    timestamp: float
    frame_timestamp: float | None = None
    offset_deg: float | None = None
    offset_deg_x: float | None = None
    offset_deg_y: float | None = None
    target_id: str | None = None
    reaction_seconds: float | None = None
    overcorrected: bool = False


class ShotStats(BaseModel):
    detected_shots: int = 0
    aligned_shots: int = 0
    deviation: DeviationStats = Field(default_factory=DeviationStats)
    vertical_bias: BiasStats = Field(default_factory=BiasStats)
    horizontal_bias: BiasStats = Field(default_factory=BiasStats)
    mean_reaction_seconds: float | None = None
    median_reaction_seconds: float | None = None
    # How many engagements the reaction figures rest on. Exposed because a
    # target that never leaves the screen, as on an aim trainer map, yields a
    # single span and therefore a single reaction, which otherwise reads as a
    # measured average.
    reaction_samples: int = 0
    overcorrection_count: int = 0
    overcorrection_ratio: float = 0.0
    shots: list[ShotMetric] = Field(default_factory=list)
    source: str = "none"
    message: str = ""


class SessionMetrics(BaseModel):
    """Session-level aim diagnostics, all angles in degrees."""

    session_id: str = "anonymous"
    job_id: str | None = None
    created_at: str = ""
    video_name: str | None = None
    duration_seconds: float = 0.0
    frame_width: int = 0
    frame_height: int = 0
    fov_deg: float = 0.0
    effective_horizontal_fov_deg: float = 0.0
    focal_length_px: float = 0.0
    sample_rate: float = 0.0
    sampled_frames: int = 0
    frames_with_target: int = 0
    target_visibility_ratio: float = 0.0
    placement_deviation: DeviationStats = Field(default_factory=DeviationStats)
    vertical_bias: BiasStats = Field(default_factory=BiasStats)
    horizontal_bias: BiasStats = Field(default_factory=BiasStats)
    effective_tracking: TrackingStats = Field(default_factory=TrackingStats)
    deviation_histogram: list[HistogramBin] = Field(default_factory=list)
    shots: ShotStats = Field(default_factory=ShotStats)
    headline: str = ""


class RecordingEntry(BaseModel):
    """A video sitting in the media root, ready to be submitted for analysis."""

    name: str
    path: str
    size_bytes: int
    modified_at: str
    analyzed: bool = False


class SessionSummary(BaseModel):
    """Index entry for a stored session."""

    job_id: str
    session_id: str
    created_at: str
    video_name: str | None = None
    duration_seconds: float = 0.0
    sampled_frames: int = 0
    mean_deviation_deg: float = 0.0
    vertical_bias_deg: float = 0.0
    on_target_ratio: float = 0.0
    detected_shots: int = 0


VisionJobResponse.model_rebuild()
