"""Score the detector against projected ground truth.

Pairing a demo with a recording gives a head box per enemy per frame without
anyone labelling anything, and this module turns that into the numbers the model
is actually judged on: how often it finds a head that was really there, how
often it invents one, and how far off it is when it does find one.

The error is reported in degrees rather than pixels or IoU because that is the
quantity the coaching metrics are built from. An IoU of 0.5 says little about
whether a deviation reading can be trusted; half a degree of centre error says
exactly how much of the measured deviation is the model's own noise.
"""

from __future__ import annotations

from dataclasses import dataclass
from statistics import fmean, median, pstdev
from typing import Sequence

from pydantic import BaseModel, Field

from .geometry import CameraModel
from .projection import ProjectedHead, intersection_over_union
from .schemas import VisionEnemy, VisionFrameResponse

# Beyond this a detection is a different target, not a sloppy box on this one.
DEFAULT_MATCH_GATE_DEG = 3.0


@dataclass(frozen=True)
class Match:
    truth_id: str
    detection_id: str
    error_deg: float
    horizontal_error_deg: float
    vertical_error_deg: float
    iou: float
    depth: float


class FrameEvaluation(BaseModel):
    timestamp: float
    visible_truth: int = 0
    detections: int = 0
    matched: int = 0
    missed: int = 0
    false_positives: int = 0


class EvaluationReport(BaseModel):
    """Detector accuracy against demo-projected ground truth."""

    frames: int = 0
    aligned_frames: int = 0
    time_offset_seconds: float = 0.0
    alignment_score: float = 0.0
    match_gate_deg: float = DEFAULT_MATCH_GATE_DEG

    total_truth: int = 0
    total_detections: int = 0
    total_matched: int = 0
    total_missed: int = 0
    total_false_positives: int = 0

    recall: float = 0.0
    precision: float = 0.0
    f1: float = 0.0

    mean_error_deg: float = 0.0
    median_error_deg: float = 0.0
    p90_error_deg: float = 0.0
    std_error_deg: float = 0.0
    mean_horizontal_error_deg: float = 0.0
    mean_vertical_error_deg: float = 0.0
    mean_iou: float = 0.0

    recall_by_distance: dict[str, float] = Field(default_factory=dict)
    per_frame: list[FrameEvaluation] = Field(default_factory=list)
    notes: str = ""


def _center(box: VisionEnemy) -> tuple[float, float]:
    return ((box.x1 + box.x2) / 2.0, (box.y1 + box.y2) / 2.0)


def match_frame(
    detections: Sequence[VisionEnemy],
    truth: Sequence[ProjectedHead],
    camera: CameraModel,
    gate_deg: float = DEFAULT_MATCH_GATE_DEG,
) -> tuple[list[Match], list[str], list[str]]:
    """Pair detections with ground-truth heads, greedily by angular proximity.

    Matching on centre separation rather than IoU because head boxes are only a
    few pixels across at range: two boxes describing the same head can overlap
    by nothing at all while sitting a fraction of a degree apart, and an IoU
    gate would score those as both a miss and a false positive.
    """
    heads = [item for item in detections if item.part == "head"]
    candidates: list[tuple[float, int, int]] = []
    for truth_index, expected in enumerate(truth):
        for detection_index, found in enumerate(heads):
            separation = camera.angular_distance_deg(
                _center(found),
                expected.center,
            )
            if separation <= gate_deg:
                candidates.append((separation, truth_index, detection_index))

    candidates.sort()
    used_truth: set[int] = set()
    used_detection: set[int] = set()
    matches: list[Match] = []
    for separation, truth_index, detection_index in candidates:
        if truth_index in used_truth or detection_index in used_detection:
            continue
        used_truth.add(truth_index)
        used_detection.add(detection_index)

        expected = truth[truth_index]
        found = heads[detection_index]
        horizontal, vertical = camera.axis_angles_deg(
            _center(found),
            expected.center,
        )
        matches.append(
            Match(
                truth_id=expected.identifier,
                detection_id=found.id,
                error_deg=separation,
                horizontal_error_deg=horizontal,
                vertical_error_deg=vertical,
                iou=intersection_over_union(
                    (found.x1, found.y1, found.x2, found.y2),
                    (expected.x1, expected.y1, expected.x2, expected.y2),
                ),
                depth=expected.depth,
            )
        )

    missed = [
        truth[index].identifier
        for index in range(len(truth))
        if index not in used_truth
    ]
    false_positives = [
        heads[index].id
        for index in range(len(heads))
        if index not in used_detection
    ]
    return matches, missed, false_positives


DISTANCE_BANDS = (
    ("0-500", 0.0, 500.0),
    ("500-1000", 500.0, 1000.0),
    ("1000-2000", 1000.0, 2000.0),
    ("2000+", 2000.0, float("inf")),
)


def evaluate_session(
    frames: Sequence[VisionFrameResponse],
    truth_by_frame: Sequence[Sequence[ProjectedHead]],
    camera: CameraModel,
    gate_deg: float = DEFAULT_MATCH_GATE_DEG,
    time_offset_seconds: float = 0.0,
    alignment_score: float = 0.0,
    notes: str = "",
) -> EvaluationReport:
    """Aggregate per-frame matching into one accuracy report.

    ``truth_by_frame`` is positional: entry *i* holds the ground truth for
    ``frames[i]``, already aligned in time by the caller.
    """
    if len(frames) != len(truth_by_frame):
        raise ValueError("frames and ground truth must be the same length")

    errors: list[float] = []
    horizontals: list[float] = []
    verticals: list[float] = []
    ious: list[float] = []
    per_frame: list[FrameEvaluation] = []
    band_totals = {name: [0, 0] for name, _, _ in DISTANCE_BANDS}

    total_truth = 0
    total_detections = 0
    total_matched = 0
    total_missed = 0
    total_false_positives = 0
    aligned_frames = 0

    for frame, truth in zip(frames, truth_by_frame):
        visible = [head for head in truth if head.on_screen]
        detections = [enemy for enemy in frame.enemies if enemy.part == "head"]
        if visible or detections:
            aligned_frames += 1

        matches, missed, false_positives = match_frame(
            detections,
            visible,
            camera,
            gate_deg,
        )

        for match in matches:
            errors.append(match.error_deg)
            horizontals.append(match.horizontal_error_deg)
            verticals.append(match.vertical_error_deg)
            ious.append(match.iou)

        for head in visible:
            for name, lower, upper in DISTANCE_BANDS:
                if lower <= head.depth < upper:
                    band_totals[name][1] += 1
                    break
        for match in matches:
            for name, lower, upper in DISTANCE_BANDS:
                if lower <= match.depth < upper:
                    band_totals[name][0] += 1
                    break

        total_truth += len(visible)
        total_detections += len(detections)
        total_matched += len(matches)
        total_missed += len(missed)
        total_false_positives += len(false_positives)

        per_frame.append(
            FrameEvaluation(
                timestamp=frame.timestamp,
                visible_truth=len(visible),
                detections=len(detections),
                matched=len(matches),
                missed=len(missed),
                false_positives=len(false_positives),
            )
        )

    recall = total_matched / total_truth if total_truth else 0.0
    precision = total_matched / total_detections if total_detections else 0.0
    f1 = (
        2 * precision * recall / (precision + recall)
        if precision + recall > 0.0
        else 0.0
    )

    return EvaluationReport(
        frames=len(frames),
        aligned_frames=aligned_frames,
        time_offset_seconds=time_offset_seconds,
        alignment_score=alignment_score,
        match_gate_deg=gate_deg,
        total_truth=total_truth,
        total_detections=total_detections,
        total_matched=total_matched,
        total_missed=total_missed,
        total_false_positives=total_false_positives,
        recall=recall,
        precision=precision,
        f1=f1,
        mean_error_deg=fmean(errors) if errors else 0.0,
        median_error_deg=median(errors) if errors else 0.0,
        p90_error_deg=_percentile(sorted(errors), 0.9) if errors else 0.0,
        std_error_deg=pstdev(errors) if len(errors) > 1 else 0.0,
        mean_horizontal_error_deg=fmean(horizontals) if horizontals else 0.0,
        mean_vertical_error_deg=fmean(verticals) if verticals else 0.0,
        mean_iou=fmean(ious) if ious else 0.0,
        recall_by_distance={
            name: (matched / total if total else 0.0)
            for name, (matched, total) in band_totals.items()
        },
        per_frame=per_frame,
        notes=notes,
    )


def _percentile(ordered: Sequence[float], fraction: float) -> float:
    if not ordered:
        return 0.0
    if len(ordered) == 1:
        return ordered[0]
    position = fraction * (len(ordered) - 1)
    lower = int(position)
    upper = min(lower + 1, len(ordered) - 1)
    weight = position - lower
    return ordered[lower] * (1.0 - weight) + ordered[upper] * weight
