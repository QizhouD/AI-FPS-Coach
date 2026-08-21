"""Compare PyTorch and ONNX Ultralytics predictions on the same images."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np


IMAGE_SUFFIXES = {".jpg", ".jpeg", ".png", ".bmp", ".webp"}


def _iou(left: np.ndarray, right: np.ndarray) -> float:
    x1 = max(float(left[0]), float(right[0]))
    y1 = max(float(left[1]), float(right[1]))
    x2 = min(float(left[2]), float(right[2]))
    y2 = min(float(left[3]), float(right[3]))
    intersection = max(0.0, x2 - x1) * max(0.0, y2 - y1)
    left_area = max(0.0, float(left[2] - left[0])) * max(
        0.0, float(left[3] - left[1])
    )
    right_area = max(0.0, float(right[2] - right[0])) * max(
        0.0, float(right[3] - right[1])
    )
    union = left_area + right_area - intersection
    return intersection / union if union else 0.0


def _predict(model, image, imgsz: int, confidence: float) -> list[dict]:
    result = model.predict(
        source=image,
        imgsz=imgsz,
        conf=confidence,
        verbose=False,
    )[0]
    boxes = result.boxes
    xyxy = boxes.xyxy.detach().cpu().numpy()
    scores = boxes.conf.detach().cpu().numpy()
    classes = boxes.cls.detach().cpu().numpy().astype(int)
    return [
        {"box": box, "confidence": float(score), "class": int(class_id)}
        for box, score, class_id in zip(xyxy, scores, classes)
    ]


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--pytorch", required=True, help="PyTorch .pt weights")
    parser.add_argument("--onnx", required=True, help="Exported ONNX weights")
    parser.add_argument("--image-dir", required=True)
    parser.add_argument("--imgsz", type=int, default=1280)
    parser.add_argument("--confidence", type=float, default=0.25)
    args = parser.parse_args()

    try:
        import cv2
        from ultralytics import YOLO
    except ImportError as exc:  # pragma: no cover - optional runtime dependency
        raise SystemExit(
            "ultralytics and opencv-python are required for model comparison"
        ) from exc

    image_paths = sorted(
        path
        for path in Path(args.image_dir).expanduser().rglob("*")
        if path.suffix.lower() in IMAGE_SUFFIXES
    )
    if not image_paths:
        raise SystemExit("No images found in --image-dir")

    pytorch_model = YOLO(args.pytorch)
    onnx_model = YOLO(args.onnx)
    ious: list[float] = []
    center_errors: list[float] = []
    confidence_deltas: list[float] = []
    pt_only = 0
    onnx_only = 0
    paired = 0

    for image_path in image_paths:
        image = cv2.imread(str(image_path))
        if image is None:
            continue
        pt_predictions = _predict(pytorch_model, image, args.imgsz, args.confidence)
        onnx_predictions = _predict(onnx_model, image, args.imgsz, args.confidence)
        matched_onnx: set[int] = set()
        for pt in pt_predictions:
            candidates = [
                (index, candidate)
                for index, candidate in enumerate(onnx_predictions)
                if index not in matched_onnx and candidate["class"] == pt["class"]
            ]
            if not candidates:
                pt_only += 1
                continue
            index, candidate = max(
                candidates,
                key=lambda item: _iou(pt["box"], item[1]["box"]),
            )
            matched_onnx.add(index)
            left = pt["box"]
            right = candidate["box"]
            diagonal = max(
                1.0,
                float(np.hypot(image.shape[1], image.shape[0])),
            )
            left_center = np.array([(left[0] + left[2]) / 2, (left[1] + left[3]) / 2])
            right_center = np.array(
                [(right[0] + right[2]) / 2, (right[1] + right[3]) / 2]
            )
            ious.append(_iou(left, right))
            center_errors.append(float(np.linalg.norm(left_center - right_center) / diagonal))
            confidence_deltas.append(abs(pt["confidence"] - candidate["confidence"]))
            paired += 1
        onnx_only += len(onnx_predictions) - len(matched_onnx)

    def mean(values: list[float]) -> float:
        return float(np.mean(values)) if values else 0.0

    print(
        json.dumps(
            {
                "images": len(image_paths),
                "paired_detections": paired,
                "mean_iou": mean(ious),
                "mean_normalized_center_error": mean(center_errors),
                "mean_confidence_delta": mean(confidence_deltas),
                "pytorch_only_detections": pt_only,
                "onnx_only_detections": onnx_only,
            },
            indent=2,
        )
    )


if __name__ == "__main__":
    main()
