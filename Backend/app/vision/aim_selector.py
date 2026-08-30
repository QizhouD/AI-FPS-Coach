from __future__ import annotations

from dataclasses import dataclass
from math import hypot
from typing import Iterable

from .geometry import CameraModel

# Where the head sits inside a full-body box, as a fraction of its height. A CS2
# player stands 72 units with the head centre about 4.5 units below the crown.
#
# Used when a player is boxed but their head is not, which is the normal case at
# range: the head is only a handful of pixels and the detector finds the larger
# body instead. Before this existed the nearest *head* box could belong to a
# completely different player, and the reported deviation was then an artifact of
# tens of degrees rather than a measurement of the player's aim.
#
# Measured against 44 headshot kills with server-confirmed head hits, the
# fallback moved the median error at those instants from 2.48 to 0.39 degrees and
# cut deviations above 5 degrees from 34% of kills to 7%. The result barely moves
# across fractions from 0.04 to 0.12, because at these ranges the whole body
# spans few enough pixels that the offset within the box is sub-pixel in angle,
# so this is a geometric constant rather than a fitted one.
HEAD_FRACTION_OF_BODY = 0.06


@dataclass(frozen=True)
class AimCandidate:
    identifier: str
    team: str
    part: str
    x1: float
    y1: float
    x2: float
    y2: float
    confidence: float

    @property
    def center(self) -> tuple[float, float]:
        return ((self.x1 + self.x2) * 0.5, (self.y1 + self.y2) * 0.5)

    @property
    def head_point(self) -> tuple[float, float]:
        """Where to aim on this candidate.

        A head box is aimed at directly. A body box stands in for its owner's
        undetected head, which sits near the top of the box rather than at its
        centre; aiming at the centre of a body would report a deviation of the
        player's chest height at every range.
        """
        if self.part == "head":
            return self.center
        return (
            (self.x1 + self.x2) * 0.5,
            self.y1 + (self.y2 - self.y1) * HEAD_FRACTION_OF_BODY,
        )


class AimSmoother:
    def __init__(self, alpha: float = 0.45) -> None:
        if not 0.0 < alpha <= 1.0:
            raise ValueError("alpha must be in the range (0, 1].")
        self.alpha = alpha
        self._last: tuple[float, float] | None = None

    def reset(self) -> None:
        self._last = None

    def update(self, point: tuple[float, float]) -> tuple[float, float]:
        if self._last is None:
            self._last = point
            return point
        x = self._last[0] + self.alpha * (point[0] - self._last[0])
        y = self._last[1] + self.alpha * (point[1] - self._last[1])
        self._last = (x, y)
        return self._last


def select_recommended_aim(
    candidates: Iterable[AimCandidate],
    reference_point: tuple[float, float] = (0.5, 0.5),
    smoother: AimSmoother | None = None,
    camera: CameraModel | None = None,
) -> dict[str, object]:
    """Pick the head nearest the crosshair and measure the deviation to it.

    The returned point is smoothed for display, but every ``offset_*`` field is
    measured against the unsmoothed detection. Smoothing exists to stop the
    overlay marker jittering; feeding it into the statistics would mean
    reporting a low-pass filtered version of the player's aim rather than the
    aim itself.

    Offsets are ``None`` rather than zero when no player is visible, so that
    "nothing to aim at" stays distinguishable from "perfectly on target"
    downstream. Collapsing the two would drag every aggregate toward zero in
    exactly the frames that carry no information.

    A player boxed without their head still counts as a target, with the head
    position inferred from the body box. Restricting this to detected head boxes
    meant that whenever the true target's head was too small to detect, the
    nearest head belonged to somebody else and the deviation described the wrong
    player. ``target_source`` records which of the two produced the answer.
    """
    ranked = [
        candidate
        for candidate in candidates
        if candidate.part == "head" or candidate.part == "body"
    ]
    if not ranked:
        return {
            "x": reference_point[0],
            "y": reference_point[1],
            "target_id": None,
            "target_source": None,
            "confidence": 0.0,
            "offset_x": None,
            "offset_y": None,
            "offset_deg_x": None,
            "offset_deg_y": None,
            "offset_deg": None,
        }

    def distance_of(candidate: AimCandidate) -> tuple[float, int]:
        point = candidate.head_point
        if camera is not None:
            primary = camera.angular_distance_deg(point, reference_point)
        else:
            # Without a camera model, fall back to normalized pixel distance.
            primary = hypot(point[0] - reference_point[0], point[1] - reference_point[1])
        # A detected head outranks an inferred one at equal distance, since it is
        # a measurement rather than an assumption.
        return (primary, 0 if candidate.part == "head" else 1)

    target = min(ranked, key=distance_of)
    raw_point = target.head_point
    display_point = smoother.update(raw_point) if smoother else raw_point

    angles: tuple[float | None, float | None, float | None] = (None, None, None)
    if camera is not None:
        horizontal, vertical = camera.axis_angles_deg(raw_point, reference_point)
        angles = (
            horizontal,
            vertical,
            camera.angular_distance_deg(raw_point, reference_point),
        )

    return {
        "x": display_point[0],
        "y": display_point[1],
        "target_id": target.identifier,
        "target_source": "head" if target.part == "head" else "inferred_head",
        "confidence": target.confidence,
        "offset_x": raw_point[0] - reference_point[0],
        "offset_y": raw_point[1] - reference_point[1],
        "offset_deg_x": angles[0],
        "offset_deg_y": angles[1],
        "offset_deg": angles[2],
    }
