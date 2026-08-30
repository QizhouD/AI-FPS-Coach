"""Pixel-to-angle conversion for aim deviation measurement.

A deviation expressed in pixels cannot be compared across resolutions or fields
of view, so every reported aim metric passes through this module first. The
mapping is deliberately not ``fov / width``: a pixel near the edge of the frame
subtends a smaller angle than one beside the crosshair, and the linear
approximation only holds very close to the centre.
"""

from __future__ import annotations

from dataclasses import dataclass
from math import acos, atan, degrees, radians, sqrt, tan

# The CS2 in-game setting, expressed as a horizontal FOV at 4:3.
CS2_DEFAULT_FOV_DEG = 90.0
CS2_BASE_ASPECT = 4.0 / 3.0


@dataclass(frozen=True)
class CameraModel:
    """Maps normalized frame coordinates onto view angles.

    ``fov_deg`` is the value the player has configured in game, which Source
    engines define as the horizontal FOV at 4:3.
    """

    width: int
    height: int
    fov_deg: float = CS2_DEFAULT_FOV_DEG
    base_aspect: float = CS2_BASE_ASPECT

    def __post_init__(self) -> None:
        if self.width <= 0 or self.height <= 0:
            raise ValueError("frame dimensions must be positive")
        if not 1.0 < self.fov_deg < 179.0:
            raise ValueError("fov_deg must be between 1 and 179 degrees")
        if self.base_aspect <= 0.0:
            raise ValueError("base_aspect must be positive")

    @property
    def focal_length_px(self) -> float:
        """Pixels per unit of tangent, shared by both axes because pixels are square.

        Source engines scale Hor+: widening the aspect ratio reveals more of the
        scene horizontally instead of cropping vertically, which leaves the
        vertical FOV invariant. Deriving the focal length from the vertical axis
        is therefore correct at any aspect ratio, whereas deriving it from the
        horizontal axis would only hold at the 4:3 the setting is defined
        against. For 1920x1080 at the default FOV this yields 720 px.
        """
        half_vertical_tan = tan(radians(self.fov_deg) / 2.0) / self.base_aspect
        return (self.height / 2.0) / half_vertical_tan

    @property
    def effective_horizontal_fov_deg(self) -> float:
        """The horizontal FOV actually visible at this frame's aspect ratio."""
        return 2.0 * degrees(atan((self.width / 2.0) / self.focal_length_px))

    @property
    def vertical_fov_deg(self) -> float:
        return 2.0 * degrees(atan((self.height / 2.0) / self.focal_length_px))

    def _ray(self, point: tuple[float, float]) -> tuple[float, float, float]:
        """A view-space direction vector for a normalized frame coordinate."""
        focal = self.focal_length_px
        return (
            (point[0] - 0.5) * self.width,
            (point[1] - 0.5) * self.height,
            focal,
        )

    def axis_angles_deg(
        self,
        target: tuple[float, float],
        reference: tuple[float, float],
    ) -> tuple[float, float]:
        """Signed per-axis angles from ``reference`` to ``target``.

        Both components are phrased as the correction the player would have to
        make, which is what a coaching readout needs: positive horizontal means
        the crosshair must move right, positive vertical means it must move up.
        Aiming below the head, the most common fault at low ranks, therefore
        shows up as a positive vertical bias.
        """
        focal = self.focal_length_px
        dx_px = (target[0] - reference[0]) * self.width
        dy_px = (target[1] - reference[1]) * self.height
        horizontal = degrees(atan(dx_px / focal))
        # Frame coordinates grow downward, so a target above the crosshair has a
        # negative dy and must be negated to read as "move up".
        vertical = degrees(atan(-dy_px / focal))
        return horizontal, vertical

    def angular_distance_deg(
        self,
        target: tuple[float, float],
        reference: tuple[float, float],
    ) -> float:
        """The true angle between the two view rays.

        This is not ``hypot`` of the two axis angles. Away from the frame centre
        the per-axis decomposition overstates the separation, so the magnitude
        used in statistics comes from the dot product instead.
        """
        ax, ay, az = self._ray(target)
        bx, by, bz = self._ray(reference)
        dot = ax * bx + ay * by + az * bz
        norm = sqrt(ax * ax + ay * ay + az * az) * sqrt(bx * bx + by * by + bz * bz)
        if norm <= 0.0:
            return 0.0
        return degrees(acos(max(-1.0, min(1.0, dot / norm))))

    def pixels_per_degree_at_center(self) -> float:
        """Useful for sanity-checking a recording against known sensitivity."""
        return self.focal_length_px * radians(1.0)


def camera_from_frame(
    width: int,
    height: int,
    fov_deg: float | None = None,
) -> CameraModel | None:
    """Build a camera model, returning None when the frame size is unusable.

    Callers run per frame, so an unusable frame must degrade to "no angles"
    rather than abort the whole job.
    """
    if width <= 0 or height <= 0:
        return None
    try:
        return CameraModel(
            width=width,
            height=height,
            fov_deg=CS2_DEFAULT_FOV_DEG if fov_deg is None else fov_deg,
        )
    except ValueError:
        return None
