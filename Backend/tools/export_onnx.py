"""Export a trained Ultralytics model to an ONNX graph for parity testing."""

from __future__ import annotations

import argparse


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--weights", required=True)
    parser.add_argument("--imgsz", type=int, default=1280)
    parser.add_argument("--opset", type=int, default=15)
    args = parser.parse_args()

    from ultralytics import YOLO

    model = YOLO(args.weights)
    model.export(
        format="onnx",
        imgsz=args.imgsz,
        opset=args.opset,
        simplify=True,
        nms=False,
    )


if __name__ == "__main__":
    main()
