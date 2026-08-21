from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Any

import numpy as np

from .aim_selector import AimCandidate


class DetectorUnavailable(RuntimeError):
    """Raised when an optional model runtime or model file is unavailable."""


@dataclass
class DetectorResult:
    detections: list[AimCandidate]
    available: bool
    message: str


def normalize_label(label: str) -> tuple[str, str]:
    normalized = label.strip().lower().replace("-", "_").replace(" ", "_")
    team = "unknown"
    if normalized.startswith("ct"):
        team = "CT"
    elif normalized.startswith("t"):
        team = "T"
    part = "head" if "head" in normalized else "body"
    return team, part


class UltralyticsDetector:
    def __init__(
        self,
        model_path: str | None,
        confidence: float = 0.25,
        device: str = "cpu",
        label_filter: str | None = None,
    ) -> None:
        self.model_path = model_path
        self.confidence = confidence
        self.device = device
        self.label_filter = label_filter
        self._model: Any = None
        self._names: dict[int, str] = {}
        self._load_error: str | None = None

        if not model_path:
            self._load_error = "model path is not configured"
            return
        if not Path(model_path).is_file():
            self._load_error = f"model file does not exist: {model_path}"
            return
        try:
            from ultralytics import YOLO

            self._model = YOLO(model_path)
            names = getattr(self._model, "names", {})
            self._names = dict(names) if isinstance(names, dict) else {
                index: name for index, name in enumerate(names)
            }
        except Exception as exc:  # pragma: no cover - depends on optional runtime
            self._load_error = str(exc)

    @property
    def available(self) -> bool:
        return self._model is not None

    @property
    def status(self) -> str:
        return "ready" if self.available else (self._load_error or "unavailable")

    def detect(self, image: np.ndarray) -> DetectorResult:
        if not self.available:
            return DetectorResult([], False, self.status)

        try:  # pragma: no cover - depends on optional runtime
            predictions = self._model.predict(
                source=image,
                conf=self.confidence,
                device=self.device,
                verbose=False,
            )
            if not predictions:
                return DetectorResult([], True, "no detections")

            result = predictions[0]
            boxes = result.boxes
            xyxy = boxes.xyxy.detach().cpu().numpy()
            confidences = boxes.conf.detach().cpu().numpy()
            classes = boxes.cls.detach().cpu().numpy().astype(int)
            height, width = image.shape[:2]
            detections: list[AimCandidate] = []
            for index, (box, score, class_index) in enumerate(
                zip(xyxy, confidences, classes)
            ):
                label = str(self._names.get(int(class_index), class_index))
                if self.label_filter and self.label_filter not in label.lower():
                    continue
                team, part = normalize_label(label)
                x1, y1, x2, y2 = box.tolist()
                detections.append(
                    AimCandidate(
                        identifier=f"enemy_{index}",
                        team=team,
                        part=part,
                        x1=max(0.0, min(1.0, x1 / width)),
                        y1=max(0.0, min(1.0, y1 / height)),
                        x2=max(0.0, min(1.0, x2 / width)),
                        y2=max(0.0, min(1.0, y2 / height)),
                        confidence=float(score),
                    )
                )
            return DetectorResult(detections, True, "ok")
        except Exception as exc:
            return DetectorResult([], False, f"inference failed: {exc}")
