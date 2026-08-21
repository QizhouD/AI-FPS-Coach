from __future__ import annotations

import unittest

import numpy as np

from app.vision.aim_selector import AimCandidate, AimSmoother, select_recommended_aim
from app.vision.detector import normalize_label
from app.vision.worker import VisionInferenceEngine


class VisionTests(unittest.TestCase):
    def test_normalize_cs2_labels(self) -> None:
        self.assertEqual(normalize_label("CT_head"), ("CT", "head"))
        self.assertEqual(normalize_label("t_body"), ("T", "body"))

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
