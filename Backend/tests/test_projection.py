from __future__ import annotations

import unittest

from app.vision.geometry import CameraModel
from app.vision.projection import (
    STANDING_EYE_HEIGHT,
    angle_vectors,
    estimate_time_offset,
    eye_position,
    intersection_over_union,
    project_head,
    project_point,
)


class AngleVectorTests(unittest.TestCase):
    def test_zero_angles_look_along_positive_x(self) -> None:
        basis = angle_vectors(0.0, 0.0)
        self.assertAlmostEqual(basis.forward[0], 1.0, places=9)
        self.assertAlmostEqual(basis.forward[1], 0.0, places=9)
        self.assertAlmostEqual(basis.forward[2], 0.0, places=9)
        # The player's right hand points along -Y when facing +X.
        self.assertAlmostEqual(basis.right[1], -1.0, places=9)
        self.assertAlmostEqual(basis.up[2], 1.0, places=9)

    def test_positive_pitch_looks_downward(self) -> None:
        """Source inverts pitch relative to the usual convention."""
        basis = angle_vectors(30.0, 0.0)
        self.assertLess(basis.forward[2], 0.0)

    def test_yaw_rotates_toward_positive_y(self) -> None:
        basis = angle_vectors(0.0, 90.0)
        self.assertAlmostEqual(basis.forward[0], 0.0, places=9)
        self.assertAlmostEqual(basis.forward[1], 1.0, places=9)

    def test_the_basis_stays_orthonormal(self) -> None:
        basis = angle_vectors(23.0, 137.0)
        vectors = [basis.forward, basis.right, basis.up]
        for vector in vectors:
            length = sum(component * component for component in vector) ** 0.5
            self.assertAlmostEqual(length, 1.0, places=9)
        for first in range(3):
            for second in range(first + 1, 3):
                dot = sum(
                    a * b for a, b in zip(vectors[first], vectors[second])
                )
                self.assertAlmostEqual(dot, 0.0, places=9)


class ProjectionTests(unittest.TestCase):
    def setUp(self) -> None:
        self.camera = CameraModel(width=1920, height=1080)
        self.eye = (0.0, 0.0, 64.0)
        self.basis = angle_vectors(0.0, 0.0)

    def test_a_point_straight_ahead_lands_at_the_crosshair(self) -> None:
        projected = project_point((500.0, 0.0, 64.0), self.eye, self.basis, self.camera)
        self.assertIsNotNone(projected)
        assert projected is not None
        self.assertAlmostEqual(projected.x, 0.5, places=9)
        self.assertAlmostEqual(projected.y, 0.5, places=9)
        self.assertTrue(projected.on_screen)

    def test_a_point_to_the_players_right_lands_right_of_centre(self) -> None:
        # Facing +X, the player's right is -Y.
        projected = project_point((500.0, -100.0, 64.0), self.eye, self.basis, self.camera)
        assert projected is not None
        self.assertGreater(projected.x, 0.5)

    def test_a_higher_point_lands_above_centre(self) -> None:
        projected = project_point((500.0, 0.0, 164.0), self.eye, self.basis, self.camera)
        assert projected is not None
        self.assertLess(projected.y, 0.5)

    def test_a_point_behind_the_camera_does_not_project(self) -> None:
        self.assertIsNone(
            project_point((-500.0, 0.0, 64.0), self.eye, self.basis, self.camera)
        )

    def test_projection_and_measurement_agree(self) -> None:
        """The pairing is only meaningful if both halves share one camera.

        A point placed at a known angle must measure back as that same angle
        through the metrics path, otherwise ground-truth error would carry a
        systematic bias of its own.
        """
        from math import atan, degrees

        distance = 800.0
        lateral = 200.0
        target = (distance, -lateral, 64.0)
        projected = project_point(target, self.eye, self.basis, self.camera)
        assert projected is not None

        horizontal, _ = self.camera.axis_angles_deg(
            (projected.x, projected.y),
            (0.5, 0.5),
        )
        expected = degrees(atan(lateral / distance))
        self.assertAlmostEqual(horizontal, expected, places=6)

    def test_a_head_box_shrinks_with_distance(self) -> None:
        near = project_head(
            "near", "T", (300.0, 0.0, 0.0), self.eye, self.basis, self.camera
        )
        far = project_head(
            "far", "T", (1200.0, 0.0, 0.0), self.eye, self.basis, self.camera
        )
        assert near is not None and far is not None

        near_width = near.x2 - near.x1
        far_width = far.x2 - far.x1
        self.assertGreater(near_width, far_width)
        # Four times the distance is a quarter of the size.
        self.assertAlmostEqual(near_width / far_width, 4.0, places=6)

    def test_a_head_at_eye_level_and_range_sits_near_the_crosshair(self) -> None:
        head = project_head(
            "target", "T", (900.0, 0.0, 0.0), self.eye, self.basis, self.camera
        )
        assert head is not None
        centre_x, centre_y = head.center
        self.assertAlmostEqual(centre_x, 0.5, places=6)
        # The head centre sits just above eye level, so just above the crosshair.
        self.assertLess(centre_y, 0.5)
        self.assertAlmostEqual(centre_y, 0.5, places=2)

    def test_eye_position_raises_the_origin_from_the_feet(self) -> None:
        self.assertEqual(
            eye_position((10.0, 20.0, 30.0)),
            (10.0, 20.0, 30.0 + STANDING_EYE_HEIGHT),
        )


class IouTests(unittest.TestCase):
    def test_identical_boxes_overlap_completely(self) -> None:
        box = (0.1, 0.1, 0.2, 0.2)
        self.assertAlmostEqual(intersection_over_union(box, box), 1.0)

    def test_disjoint_boxes_do_not_overlap(self) -> None:
        self.assertEqual(
            intersection_over_union((0.0, 0.0, 0.1, 0.1), (0.5, 0.5, 0.6, 0.6)),
            0.0,
        )

    def test_half_overlap(self) -> None:
        value = intersection_over_union((0.0, 0.0, 0.2, 0.1), (0.1, 0.0, 0.3, 0.1))
        self.assertAlmostEqual(value, 1.0 / 3.0, places=9)


class TimeOffsetTests(unittest.TestCase):
    def test_a_constant_lag_is_recovered(self) -> None:
        video = [1.0, 2.5, 2.6, 7.2, 11.0]
        lag = 4.25
        demo = [event + lag for event in video]

        offset, score = estimate_time_offset(video, demo)

        self.assertAlmostEqual(offset, lag, places=2)
        self.assertAlmostEqual(score, 1.0)

    def test_extra_demo_events_do_not_prevent_alignment(self) -> None:
        """The demo holds the whole match; the recording holds only part of it."""
        video = [1.0, 1.1, 5.0]
        lag = -3.5
        demo = sorted([event + lag for event in video] + [20.0, 33.0, 41.5])

        offset, score = estimate_time_offset(video, demo)

        self.assertAlmostEqual(offset, lag, places=2)
        self.assertAlmostEqual(score, 1.0)

    def test_unrelated_trains_score_low(self) -> None:
        offset, score = estimate_time_offset([1.0, 2.0, 3.0], [])
        self.assertEqual(offset, 0.0)
        self.assertEqual(score, 0.0)

    def test_a_partial_match_is_reported_as_partial(self) -> None:
        video = [1.0, 2.0, 3.0, 4.0]
        demo = [1.5, 2.5]
        _, score = estimate_time_offset(video, demo)
        self.assertLess(score, 1.0)


if __name__ == "__main__":
    unittest.main()
