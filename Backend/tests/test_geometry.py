from __future__ import annotations

import unittest
from math import atan, degrees, hypot

from app.vision.geometry import CameraModel, camera_from_frame


class GeometryTests(unittest.TestCase):
    def test_focal_length_for_1080p_at_default_fov(self) -> None:
        """Hor+ scaling puts the 1080p focal length at exactly 720 px."""
        camera = CameraModel(width=1920, height=1080)
        self.assertAlmostEqual(camera.focal_length_px, 720.0, places=6)

    def test_widescreen_widens_horizontally_and_preserves_vertical_fov(self) -> None:
        wide = CameraModel(width=1920, height=1080)
        classic = CameraModel(width=1024, height=768)

        # The configured 90 degrees is defined at 4:3 and holds there.
        self.assertAlmostEqual(classic.effective_horizontal_fov_deg, 90.0, places=6)
        # 16:9 reveals more of the scene rather than cropping.
        self.assertAlmostEqual(wide.effective_horizontal_fov_deg, 106.26, places=2)
        # Hor+ means the vertical extent is unchanged between the two.
        self.assertAlmostEqual(
            wide.vertical_fov_deg,
            classic.vertical_fov_deg,
            places=6,
        )

    def test_conversion_is_non_linear_across_the_frame(self) -> None:
        """A pixel near the edge subtends less angle than one beside the crosshair.

        This is the reason the naive fov/width conversion is not used.
        """
        camera = CameraModel(width=1920, height=1080)
        centre_step, _ = camera.axis_angles_deg((0.55, 0.5), (0.5, 0.5))
        near_edge_start, _ = camera.axis_angles_deg((0.90, 0.5), (0.5, 0.5))
        near_edge_end, _ = camera.axis_angles_deg((0.95, 0.5), (0.5, 0.5))
        edge_step = near_edge_end - near_edge_start

        self.assertGreater(centre_step, edge_step)
        naive = 0.05 * camera.effective_horizontal_fov_deg
        self.assertNotAlmostEqual(centre_step, naive, places=2)

    def test_sign_convention_matches_the_required_correction(self) -> None:
        camera = CameraModel(width=1920, height=1080)

        # Target above the crosshair: the player is aiming low, so move up.
        _, vertical = camera.axis_angles_deg((0.5, 0.4), (0.5, 0.5))
        self.assertGreater(vertical, 0.0)

        # Target to the right: move right.
        horizontal, _ = camera.axis_angles_deg((0.6, 0.5), (0.5, 0.5))
        self.assertGreater(horizontal, 0.0)

    def test_angular_distance_is_not_the_hypotenuse_of_axis_angles(self) -> None:
        """Off-centre, the per-axis decomposition overstates the true separation."""
        camera = CameraModel(width=1920, height=1080)
        target = (0.85, 0.2)
        reference = (0.5, 0.5)

        horizontal, vertical = camera.axis_angles_deg(target, reference)
        combined = camera.angular_distance_deg(target, reference)

        self.assertLess(combined, hypot(horizontal, vertical))

    def test_angular_distance_matches_atan_on_a_single_axis(self) -> None:
        camera = CameraModel(width=1920, height=1080)
        distance = camera.angular_distance_deg((0.75, 0.5), (0.5, 0.5))
        expected = degrees(atan((0.25 * 1920) / 720.0))
        self.assertAlmostEqual(distance, expected, places=6)

    def test_a_narrower_fov_magnifies_the_same_pixel_offset(self) -> None:
        wide = CameraModel(width=1920, height=1080, fov_deg=90.0)
        narrow = CameraModel(width=1920, height=1080, fov_deg=70.0)
        offset = ((0.6, 0.5), (0.5, 0.5))

        self.assertLess(
            narrow.angular_distance_deg(*offset),
            wide.angular_distance_deg(*offset),
        )

    def test_invalid_configuration_is_rejected(self) -> None:
        with self.assertRaises(ValueError):
            CameraModel(width=0, height=1080)
        with self.assertRaises(ValueError):
            CameraModel(width=1920, height=1080, fov_deg=200.0)

    def test_unusable_frames_degrade_to_no_camera(self) -> None:
        self.assertIsNone(camera_from_frame(0, 0))
        self.assertIsNone(camera_from_frame(1920, 1080, fov_deg=1000.0))
        self.assertIsNotNone(camera_from_frame(1920, 1080))


if __name__ == "__main__":
    unittest.main()
