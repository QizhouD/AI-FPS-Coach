from __future__ import annotations

from dataclasses import dataclass
from math import hypot
from typing import Iterable


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
) -> dict[str, object]:
    heads = [candidate for candidate in candidates if candidate.part == "head"]
    if not heads:
        return {
            "x": reference_point[0],
            "y": reference_point[1],
            "target_id": None,
            "confidence": 0.0,
            "offset_x": 0.0,
            "offset_y": 0.0,
        }

    target = min(
        heads,
        key=lambda candidate: hypot(
            candidate.center[0] - reference_point[0],
            candidate.center[1] - reference_point[1],
        ),
    )
    target_point = smoother.update(target.center) if smoother else target.center
    return {
        "x": target_point[0],
        "y": target_point[1],
        "target_id": target.identifier,
        "confidence": target.confidence,
        "offset_x": target_point[0] - reference_point[0],
        "offset_y": target_point[1] - reference_point[1],
    }
