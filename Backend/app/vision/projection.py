"""Project CS2 world positions into recorded screen space.

This is what makes the vision model measurable without hand-labelling anything.
A demo recorded alongside the video already knows exactly where every player
was, so projecting those positions through the same camera model the metrics use
yields ground-truth boxes for free, and the difference against what the detector
found is the model's error in degrees.

Angles follow the Source engine convention, which is not the usual one:

* ``yaw`` rotates around the vertical axis, zero along ``+X``, increasing toward
  ``+Y``.
* ``pitch`` is **positive downward**, so looking at the sky is a negative pitch.
* World units are inches; a standing player's eyes sit 64 units above the origin
  reported for them, which is at their feet.
"""

from __future__ import annotations

from dataclasses import dataclass
from math import cos, radians, sin

from .geometry import CameraModel

# Eye height above the player origin while standing, in world units.
STANDING_EYE_HEIGHT = 64.0
CROUCHING_EYE_HEIGHT = 46.0
# Rough radius of the head hitbox, used to give the projected point an extent.
HEAD_RADIUS = 5.0
# Centre of the head sits slightly above eye level.
HEAD_CENTER_ABOVE_EYES = 2.0

Vector3 = tuple[float, float, float]


@dataclass(frozen=True)
class ViewBasis:
    """The camera's orthonormal axes in world space."""

    forward: Vector3
    right: Vector3
    up: Vector3


def angle_vectors(pitch_deg: float, yaw_deg: float, roll_deg: float = 0.0) -> ViewBasis:
    """Source's ``AngleVectors``, restricted to what a first-person view needs."""
    pitch = radians(pitch_deg)
    yaw = radians(yaw_deg)
    roll = radians(roll_deg)

    sp, cp = sin(pitch), cos(pitch)
    sy, cy = sin(yaw), cos(yaw)
    sr, cr = sin(roll), cos(roll)

    forward = (cp * cy, cp * sy, -sp)
    right = (
        -sr * sp * cy + cr * sy,
        -sr * sp * sy - cr * cy,
        -sr * cp,
    )
    up = (
        cr * sp * cy + sr * sy,
        cr * sp * sy - sr * cy,
        cr * cp,
    )
    return ViewBasis(forward=forward, right=right, up=up)


def _dot(a: Vector3, b: Vector3) -> float:
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def _subtract(a: Vector3, b: Vector3) -> Vector3:
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


@dataclass(frozen=True)
class ProjectedPoint:
    """A world point placed on the frame, in normalized coordinates."""

    x: float
    y: float
    depth: float
    on_screen: bool


def project_point(
    world: Vector3,
    eye: Vector3,
    basis: ViewBasis,
    camera: CameraModel,
) -> ProjectedPoint | None:
    """Place a world point on the frame, or None when it is behind the camera.

    Uses the same focal length as the deviation metrics, so a ground-truth box
    and a detected box are measured against one another in one consistent
    camera, and any error in the configured FOV shifts both alike.
    """
    delta = _subtract(world, eye)
    depth = _dot(delta, basis.forward)
    if depth <= 1e-6:
        return None

    focal = camera.focal_length_px
    horizontal = _dot(delta, basis.right)
    vertical = _dot(delta, basis.up)

    x_px = camera.width / 2.0 + focal * (horizontal / depth)
    # Frame coordinates grow downward while the up axis grows upward.
    y_px = camera.height / 2.0 - focal * (vertical / depth)

    x = x_px / camera.width
    y = y_px / camera.height
    return ProjectedPoint(
        x=x,
        y=y,
        depth=depth,
        on_screen=0.0 <= x <= 1.0 and 0.0 <= y <= 1.0,
    )


@dataclass(frozen=True)
class ProjectedHead:
    identifier: str
    team: str
    x1: float
    y1: float
    x2: float
    y2: float
    depth: float
    on_screen: bool

    @property
    def center(self) -> tuple[float, float]:
        return ((self.x1 + self.x2) / 2.0, (self.y1 + self.y2) / 2.0)


def project_head(
    identifier: str,
    team: str,
    player_origin: Vector3,
    eye: Vector3,
    basis: ViewBasis,
    camera: CameraModel,
    eye_height: float = STANDING_EYE_HEIGHT,
) -> ProjectedHead | None:
    """Turn a player's world position into a ground-truth head box.

    The head is treated as a sphere so its on-screen size follows distance
    without needing the animated skeleton, which a demo does not expose anyway.
    """
    head_world = (
        player_origin[0],
        player_origin[1],
        player_origin[2] + eye_height + HEAD_CENTER_ABOVE_EYES,
    )
    projected = project_point(head_world, eye, basis, camera)
    if projected is None:
        return None

    # Angular radius of the sphere, converted to a pixel half-extent.
    half_px = camera.focal_length_px * (HEAD_RADIUS / projected.depth)
    half_x = half_px / camera.width
    half_y = half_px / camera.height

    return ProjectedHead(
        identifier=identifier,
        team=team,
        x1=projected.x - half_x,
        y1=projected.y - half_y,
        x2=projected.x + half_x,
        y2=projected.y + half_y,
        depth=projected.depth,
        on_screen=projected.on_screen,
    )


def eye_position(player_origin: Vector3, eye_height: float = STANDING_EYE_HEIGHT) -> Vector3:
    return (player_origin[0], player_origin[1], player_origin[2] + eye_height)


def intersection_over_union(
    first: tuple[float, float, float, float],
    second: tuple[float, float, float, float],
) -> float:
    ax1, ay1, ax2, ay2 = first
    bx1, by1, bx2, by2 = second
    overlap_width = min(ax2, bx2) - max(ax1, bx1)
    overlap_height = min(ay2, by2) - max(ay1, by1)
    if overlap_width <= 0.0 or overlap_height <= 0.0:
        return 0.0
    overlap = overlap_width * overlap_height
    area_a = max(0.0, ax2 - ax1) * max(0.0, ay2 - ay1)
    area_b = max(0.0, bx2 - bx1) * max(0.0, by2 - by1)
    union = area_a + area_b - overlap
    return overlap / union if union > 0.0 else 0.0


def estimate_time_offset(
    video_events: list[float],
    demo_events: list[float],
    search_seconds: float = 30.0,
    resolution: float = 0.005,
) -> tuple[float, float]:
    """Align the recording to the demo by matching two sparse event trains.

    The video and the demo start at unrelated moments, and guessing the lag by
    hand is both tedious and the largest source of error in the whole pairing.
    Shots solve it: audio onsets give fire times in the recording, the demo
    gives them exactly, and the offset that best matches the two is the lag.

    Returns the offset to add to a video timestamp to reach demo time, together
    with a score in [0, 1] for how many video shots it accounts for. A low score
    means the alignment should not be trusted.
    """
    if not video_events or not demo_events:
        return 0.0, 0.0

    tolerance = 0.05
    steps = int(2 * search_seconds / resolution) + 1
    demo_sorted = sorted(demo_events)

    best_offset = 0.0
    best_score = 0.0
    for step in range(steps):
        offset = -search_seconds + step * resolution
        matched = sum(
            1
            for event in video_events
            if _has_match(event + offset, demo_sorted, tolerance)
        )
        score = matched / len(video_events)
        if score > best_score:
            best_score = score
            best_offset = offset

    if best_score <= 0.0:
        return 0.0, 0.0

    # Any offset within the tolerance of the true lag scores identically, so the
    # coarse search returns the near edge of a plateau rather than its centre.
    # Averaging the residuals of the pairs it matched recovers the lag to well
    # below the search resolution, which matters because this error carries
    # straight through into every ground-truth box.
    residuals = [
        nearest - (event + best_offset)
        for event in video_events
        if (nearest := _nearest(event + best_offset, demo_sorted, tolerance)) is not None
    ]
    if residuals:
        best_offset += sum(residuals) / len(residuals)

    return best_offset, best_score


def _nearest(
    value: float,
    ordered: list[float],
    tolerance: float,
) -> float | None:
    from bisect import bisect_left

    index = bisect_left(ordered, value)
    best: float | None = None
    best_gap = tolerance
    for candidate_index in (index - 1, index):
        if 0 <= candidate_index < len(ordered):
            candidate = ordered[candidate_index]
            gap = abs(candidate - value)
            if gap <= best_gap:
                best_gap = gap
                best = candidate
    return best


def _has_match(value: float, ordered: list[float], tolerance: float) -> bool:
    return _nearest(value, ordered, tolerance) is not None
