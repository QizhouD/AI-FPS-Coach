from __future__ import annotations

import unittest
from math import atan, degrees

import numpy as np

from app.vision.aim_selector import (
    HEAD_FRACTION_OF_BODY,
    AimCandidate,
    AimSmoother,
    select_recommended_aim,
)
from app.vision.detector import (
    MAX_IMAGE_SIZE,
    MIN_IMAGE_SIZE,
    image_size_for,
    normalize_label,
)
from app.vision.geometry import CameraModel
from app.vision.worker import VisionInferenceEngine


class VisionTests(unittest.TestCase):
    def test_normalize_cs2_labels(self) -> None:
        self.assertEqual(normalize_label("CT_head"), ("CT", "head"))
        self.assertEqual(normalize_label("t_body"), ("T", "body"))

    def test_1080p_footage_is_not_downscaled(self) -> None:
        """The whole point: 640 leaves a head at ~3 px and it stops existing."""
        self.assertEqual(image_size_for(1920, 1080), 1920)

    def test_inference_size_never_exceeds_the_source(self) -> None:
        """Upscaling past native resolution only costs time."""
        self.assertEqual(image_size_for(1280, 720), 1280)

    def test_small_frames_are_floored_not_shrunk_further(self) -> None:
        self.assertEqual(image_size_for(320, 240), MIN_IMAGE_SIZE)

    def test_oversized_footage_is_capped(self) -> None:
        self.assertEqual(image_size_for(3840, 2160), MAX_IMAGE_SIZE)

    def test_explicit_setting_wins_and_snaps_to_stride(self) -> None:
        self.assertEqual(image_size_for(1920, 1080, 1000), 992)
        self.assertEqual(image_size_for(1920, 1080, 640), 640)

    def test_every_size_is_a_multiple_of_the_model_stride(self) -> None:
        for width, height in ((1920, 1080), (1280, 720), (2560, 1440), (854, 480)):
            self.assertEqual(image_size_for(width, height) % 32, 0)

    def test_selects_head_nearest_to_reference(self) -> None:
        candidates = [
            AimCandidate("far", "T", "head", 0.8, 0.8, 0.9, 0.9, 0.95),
            AimCandidate("near", "T", "head", 0.52, 0.48, 0.56, 0.54, 0.8),
            AimCandidate("body", "T", "body", 0.5, 0.5, 0.6, 0.7, 0.99),
        ]
        result = select_recommended_aim(candidates, (0.5, 0.5))
        self.assertEqual(result["target_id"], "near")
        self.assertAlmostEqual(result["x"], 0.54)
        self.assertAlmostEqual(result["y"], 0.51)

    def test_smoothing_reduces_step_change(self) -> None:
        smoother = AimSmoother(alpha=0.5)
        self.assertEqual(smoother.update((0.5, 0.5)), (0.5, 0.5))
        self.assertEqual(smoother.update((0.9, 0.9)), (0.7, 0.7))

    def test_offsets_measure_the_unsmoothed_detection(self) -> None:
        """Smoothing is for the overlay marker; statistics must see the raw aim."""
        candidates = [AimCandidate("head", "T", "head", 0.7, 0.7, 0.8, 0.8, 0.9)]
        smoother = AimSmoother(alpha=0.5)
        smoother.update((0.5, 0.5))

        result = select_recommended_aim(candidates, (0.5, 0.5), smoother)

        # The displayed point is dragged halfway toward the target...
        self.assertAlmostEqual(result["x"], 0.625)
        # ...while the measurement still reports the full deviation.
        self.assertAlmostEqual(result["offset_x"], 0.25)
        self.assertAlmostEqual(result["offset_y"], 0.25)

    def test_missing_target_reports_no_offset_rather_than_zero(self) -> None:
        result = select_recommended_aim([], (0.5, 0.5))
        self.assertIsNone(result["target_id"])
        self.assertIsNone(result["target_source"])
        self.assertIsNone(result["offset_x"])
        self.assertIsNone(result["offset_y"])
        self.assertIsNone(result["offset_deg"])

    def test_a_detected_head_beats_an_inferred_one_at_equal_distance(self) -> None:
        """Prefer the measurement over the assumption when both are available."""
        camera = CameraModel(width=1920, height=1080)
        candidates = [
            AimCandidate("head", "T", "head", 0.49, 0.49, 0.51, 0.51, 0.5),
            AimCandidate("body", "T", "body", 0.49, 0.49, 0.51, 0.55, 0.99),
        ]
        result = select_recommended_aim(candidates, (0.5, 0.5), camera=camera)
        self.assertEqual(result["target_id"], "head")
        self.assertEqual(result["target_source"], "head")

    def test_a_body_at_the_crosshair_beats_a_distant_head(self) -> None:
        """The failure this fixes: measuring somebody else's head.

        At range the detector boxes a player but not their head. Ranking only
        detected heads then picked a nearer bot far off-axis, and the reported
        deviation was tens of degrees of pure artifact. Measured against 44
        server-confirmed headshots, this took the median error at those instants
        from 2.48 to 0.39 degrees.
        """
        camera = CameraModel(width=1920, height=1080)
        candidates = [
            # The player being shot: boxed, head too small to detect.
            AimCandidate("target_body", "T", "body", 0.495, 0.49, 0.505, 0.53, 0.6),
            # A closer bot well off to the side, whose head did get detected.
            AimCandidate("other_head", "T", "head", 0.20, 0.48, 0.24, 0.52, 0.9),
        ]
        result = select_recommended_aim(candidates, (0.5, 0.5), camera=camera)

        self.assertEqual(result["target_id"], "target_body")
        self.assertEqual(result["target_source"], "inferred_head")
        self.assertLess(
            result["offset_deg"],
            2.0,
            "the crosshair is on the boxed player, so the deviation must be small",
        )

    def test_the_inferred_point_sits_near_the_top_of_the_body(self) -> None:
        """Aiming at a body's centre would report its chest height as error."""
        candidate = AimCandidate("body", "T", "body", 0.4, 0.20, 0.6, 0.80, 0.9)
        head_x, head_y = candidate.head_point
        self.assertAlmostEqual(head_x, 0.5)
        # 0.6 of frame height tall, so the head sits 0.06 * 0.6 below the crown.
        self.assertAlmostEqual(head_y, 0.20 + 0.6 * HEAD_FRACTION_OF_BODY)
        self.assertLess(head_y, (0.20 + 0.80) / 2.0, "must be above the body centre")

    def test_a_head_box_is_aimed_at_directly(self) -> None:
        candidate = AimCandidate("head", "T", "head", 0.4, 0.4, 0.6, 0.6, 0.9)
        self.assertEqual(candidate.head_point, candidate.center)

    def test_angles_are_reported_when_a_camera_is_supplied(self) -> None:
        camera = CameraModel(width=1920, height=1080)
        # One head directly above the crosshair by a tenth of the frame height.
        candidates = [AimCandidate("head", "T", "head", 0.49, 0.38, 0.51, 0.42, 0.9)]
        result = select_recommended_aim(candidates, (0.5, 0.5), camera=camera)

        # 0.1 * 1080 = 108 px above centre, at a focal length of 720 px.
        expected = degrees(atan(108.0 / 720.0))
        self.assertAlmostEqual(result["offset_deg_y"], expected, places=6)
        self.assertGreater(result["offset_deg_y"], 0.0, "above centre must read as move up")

    def test_engine_returns_contract_without_models(self) -> None:
        engine = VisionInferenceEngine()
        result = engine.analyze_image(np.zeros((32, 64, 3), dtype=np.uint8))
        self.assertEqual(result.frame_width, 64)
        self.assertEqual(result.frame_height, 32)
        self.assertTrue(result.actual_crosshair.visible)
        self.assertEqual(
            result.actual_crosshair.source,
            "screen_center_baseline",
        )
        self.assertEqual(result.actual_crosshair.x, 0.5)
        self.assertEqual(result.actual_crosshair.y, 0.5)
        self.assertIsNone(result.recommended_aim.target_id)
        self.assertEqual(result.recommended_aim.x, 0.5)

    def test_crosshair_baseline_can_be_disabled(self) -> None:
        engine = VisionInferenceEngine(crosshair_baseline=False)
        result = engine.analyze_image(np.zeros((32, 64, 3), dtype=np.uint8))
        self.assertFalse(result.actual_crosshair.visible)
        self.assertEqual(result.actual_crosshair.source, "none")


if __name__ == "__main__":
    unittest.main()
