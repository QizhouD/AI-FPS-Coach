from __future__ import annotations

import numpy as np

from .detector import UltralyticsDetector


class CrosshairDetector:
    def __init__(
        self,
        model_path: str | None,
        confidence: float = 0.25,
        device: str = "cpu",
        use_center_baseline: bool = True,
    ) -> None:
        self.detector = UltralyticsDetector(
            model_path=model_path,
            confidence=confidence,
            device=device,
            label_filter="crosshair",
        )
        self.use_center_baseline = use_center_baseline

    @property
    def status(self) -> str:
        if self.detector.available or not self.use_center_baseline:
            return self.detector.status
        return f"{self.detector.status}; using screen-center baseline"

    def detect(self, image: np.ndarray) -> dict[str, object]:
        result = self.detector.detect(image)
        if not result.detections:
            if self.use_center_baseline:
                return {
                    "x": 0.5,
                    "y": 0.5,
                    "confidence": 0.5,
                    "visible": True,
                    "source": "screen_center_baseline",
                }
            return {
                "x": 0.5,
                "y": 0.5,
                "confidence": 0.0,
                "visible": False,
                "source": "none",
            }

        target = max(result.detections, key=lambda detection: detection.confidence)
        x, y = target.center
        return {
            "x": x,
            "y": y,
            "confidence": target.confidence,
            "visible": True,
            "source": "yolo",
        }
