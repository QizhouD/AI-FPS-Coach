"""Session-level aim diagnostics built from per-frame detections.

Per-frame output tells a player nothing on its own: the teaching value is in
what the whole round says about their habits. Everything here is expressed in
degrees, via :mod:`.geometry`, so numbers stay comparable across resolutions
and between recordings.

Frames where no head was visible are excluded rather than counted as zero
deviation. That distinction is why :func:`.aim_selector.select_recommended_aim`
returns ``None`` offsets instead of zeros.
"""

from __future__ import annotations

from datetime import datetime, timezone
from statistics import fmean, median, pstdev
from typing import Sequence

from .geometry import camera_from_frame
from .schemas import (
    BiasStats,
    DeviationStats,
    HistogramBin,
    SessionMetrics,
    ShotStats,
    TrackingStats,
    VisionFrameResponse,
)

# Bias smaller than this reads as noise rather than a habit worth correcting.
NEUTRAL_BIAS_DEG = 0.25
HISTOGRAM_EDGES_DEG = (0.0, 1.0, 2.0, 3.0, 5.0, 8.0, 12.0, 20.0, 35.0)


def deviation_stats(values: Sequence[float]) -> DeviationStats:
    if not values:
        return DeviationStats()
    ordered = sorted(values)
    return DeviationStats(
        count=len(ordered),
        mean_deg=fmean(ordered),
        median_deg=median(ordered),
        p90_deg=percentile_of(ordered, 0.9),
        std_deg=pstdev(ordered) if len(ordered) > 1 else 0.0,
        min_deg=ordered[0],
        max_deg=ordered[-1],
    )


def percentile_of(ordered: Sequence[float], fraction: float) -> float:
    """Linear-interpolated percentile over an already sorted sequence."""
    if not ordered:
        return 0.0
    if len(ordered) == 1:
        return ordered[0]
    position = fraction * (len(ordered) - 1)
    lower = int(position)
    upper = min(lower + 1, len(ordered) - 1)
    weight = position - lower
    return ordered[lower] * (1.0 - weight) + ordered[upper] * weight


def bias_stats(values: Sequence[float], axis: str) -> BiasStats:
    if not values:
        return BiasStats()
    mean_value = fmean(values)
    positive = sum(1 for value in values if value > 0.0)
    return BiasStats(
        count=len(values),
        mean_deg=mean_value,
        median_deg=median(values),
        std_deg=pstdev(values) if len(values) > 1 else 0.0,
        positive_ratio=positive / len(values),
        direction=_bias_direction(mean_value, axis),
    )


def _bias_direction(mean_value: float, axis: str) -> str:
    """Name the player's error, not the correction.

    A positive angle is the correction the player would need to apply, so a
    positive vertical mean means they were sitting below the head.
    """
    if abs(mean_value) < NEUTRAL_BIAS_DEG:
        return "neutral"
    if axis == "vertical":
        return "aims_low" if mean_value > 0.0 else "aims_high"
    return "aims_left" if mean_value > 0.0 else "aims_right"


def _histogram(values: Sequence[float]) -> list[HistogramBin]:
    if not values:
        return []
    bins: list[HistogramBin] = []
    for index in range(len(HISTOGRAM_EDGES_DEG)):
        lower = HISTOGRAM_EDGES_DEG[index]
        is_last = index == len(HISTOGRAM_EDGES_DEG) - 1
        upper = float("inf") if is_last else HISTOGRAM_EDGES_DEG[index + 1]
        count = sum(1 for value in values if lower <= value < upper)
        bins.append(
            HistogramBin(
                lower_deg=lower,
                upper_deg=-1.0 if is_last else upper,
                count=count,
            )
        )
    return bins


def _frame_interval(frames: Sequence[VisionFrameResponse], sample_rate: float) -> float:
    """Seconds each sampled frame represents.

    Taken from the observed timestamps where possible, because the requested
    sample rate is rounded to whole source frames inside the job and so is not
    always what was actually delivered.
    """
    timestamps = [frame.timestamp for frame in frames]
    if len(timestamps) > 1:
        gaps = [
            later - earlier
            for earlier, later in zip(timestamps, timestamps[1:])
            if later > earlier
        ]
        if gaps:
            return median(gaps)
    return 1.0 / sample_rate if sample_rate > 0.0 else 0.0


def _headline(
    placement: DeviationStats,
    vertical: BiasStats,
    tracking: TrackingStats,
) -> str:
    if placement.count == 0:
        return "No enemy heads were detected, so no aim deviation could be measured."
    parts = [
        f"Mean crosshair deviation {placement.mean_deg:.1f} deg "
        f"(median {placement.median_deg:.1f}, p90 {placement.p90_deg:.1f})."
    ]
    if vertical.direction == "aims_low":
        parts.append(
            f"Crosshair sits {abs(vertical.mean_deg):.1f} deg below head level on "
            f"average, in {vertical.positive_ratio:.0%} of measured frames."
        )
    elif vertical.direction == "aims_high":
        parts.append(
            f"Crosshair sits {abs(vertical.mean_deg):.1f} deg above head level on average."
        )
    else:
        parts.append("No systematic vertical bias.")
    parts.append(
        f"On target within {tracking.threshold_deg:.0f} deg for "
        f"{tracking.on_target_ratio:.0%} of the time a head was visible."
    )
    return " ".join(parts)


def compute_session_metrics(
    frames: Sequence[VisionFrameResponse],
    *,
    session_id: str = "anonymous",
    job_id: str | None = None,
    video_name: str | None = None,
    fov_deg: float,
    sample_rate: float = 0.0,
    tracking_threshold_deg: float = 5.0,
    shots: ShotStats | None = None,
) -> SessionMetrics:
    """Aggregate per-frame detections into the section 8.1 metrics."""
    created_at = datetime.now(timezone.utc).isoformat()
    shot_stats = shots or ShotStats()

    if not frames:
        return SessionMetrics(
            session_id=session_id,
            job_id=job_id,
            created_at=created_at,
            video_name=video_name,
            fov_deg=fov_deg,
            sample_rate=sample_rate,
            effective_tracking=TrackingStats(threshold_deg=tracking_threshold_deg),
            shots=shot_stats,
            headline="No frames were analysed.",
        )

    width = frames[0].frame_width
    height = frames[0].frame_height
    camera = camera_from_frame(width, height, fov_deg)

    deviations: list[float] = []
    verticals: list[float] = []
    horizontals: list[float] = []
    for frame in frames:
        aim = frame.recommended_aim
        if aim.target_id is None or aim.offset_deg is None:
            continue
        deviations.append(aim.offset_deg)
        if aim.offset_deg_y is not None:
            verticals.append(aim.offset_deg_y)
        if aim.offset_deg_x is not None:
            horizontals.append(aim.offset_deg_x)

    interval = _frame_interval(frames, sample_rate)
    frames_with_target = len(deviations)
    on_target = sum(1 for value in deviations if value <= tracking_threshold_deg)
    tracking = TrackingStats(
        threshold_deg=tracking_threshold_deg,
        frames_on_target=on_target,
        frames_with_target=frames_with_target,
        on_target_ratio=on_target / frames_with_target if frames_with_target else 0.0,
        on_target_seconds=on_target * interval,
        target_visible_seconds=frames_with_target * interval,
    )

    placement = deviation_stats(deviations)
    vertical = bias_stats(verticals, "vertical")
    horizontal = bias_stats(horizontals, "horizontal")

    return SessionMetrics(
        session_id=session_id,
        job_id=job_id,
        created_at=created_at,
        video_name=video_name,
        duration_seconds=frames[-1].timestamp + interval,
        frame_width=width,
        frame_height=height,
        fov_deg=fov_deg,
        effective_horizontal_fov_deg=(
            camera.effective_horizontal_fov_deg if camera else 0.0
        ),
        focal_length_px=camera.focal_length_px if camera else 0.0,
        sample_rate=sample_rate,
        sampled_frames=len(frames),
        frames_with_target=frames_with_target,
        target_visibility_ratio=frames_with_target / len(frames),
        placement_deviation=placement,
        vertical_bias=vertical,
        horizontal_bias=horizontal,
        effective_tracking=tracking,
        deviation_histogram=_histogram(deviations),
        shots=shot_stats,
        headline=_headline(placement, vertical, tracking),
    )
