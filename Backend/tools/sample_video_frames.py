"""Sample CS2 recording frames for crosshair annotation."""

from __future__ import annotations

import argparse
import json
from pathlib import Path


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True, help="Source video path")
    parser.add_argument("--output", required=True, help="Output frame directory")
    parser.add_argument("--sample-rate", type=float, default=5.0)
    parser.add_argument("--jpeg-quality", type=int, default=95)
    parser.add_argument("--max-frames", type=int, default=0)
    args = parser.parse_args()

    try:
        import cv2
    except ImportError as exc:  # pragma: no cover - optional runtime dependency
        raise SystemExit("opencv-python is required for frame sampling") from exc

    if args.sample_rate <= 0:
        raise SystemExit("--sample-rate must be greater than zero")

    input_path = Path(args.input).expanduser().resolve()
    output_path = Path(args.output).expanduser().resolve()
    output_path.mkdir(parents=True, exist_ok=True)
    if not input_path.is_file():
        raise SystemExit(f"Input video does not exist: {input_path}")

    capture = cv2.VideoCapture(str(input_path))
    if not capture.isOpened():
        raise SystemExit(f"Unable to open video: {input_path}")

    fps = capture.get(cv2.CAP_PROP_FPS) or 30.0
    total_frames = int(capture.get(cv2.CAP_PROP_FRAME_COUNT) or 0)
    step = max(1, round(fps / args.sample_rate))
    quality = max(1, min(100, args.jpeg_quality))
    manifest_path = output_path / "manifest.jsonl"
    sampled = 0
    frame_index = 0
    try:
        with manifest_path.open("w", encoding="utf-8") as manifest:
            while True:
                success, frame = capture.read()
                if not success:
                    break
                if frame_index % step == 0:
                    filename = f"frame_{frame_index:08d}.jpg"
                    destination = output_path / filename
                    if not cv2.imwrite(
                        str(destination),
                        frame,
                        [cv2.IMWRITE_JPEG_QUALITY, quality],
                    ):
                        raise SystemExit(f"Unable to write frame: {destination}")
                    manifest.write(
                        json.dumps(
                            {
                                "file": filename,
                                "frame_index": frame_index,
                                "timestamp": frame_index / fps,
                                "width": int(frame.shape[1]),
                                "height": int(frame.shape[0]),
                            },
                            ensure_ascii=False,
                        )
                        + "\n"
                    )
                    sampled += 1
                    if args.max_frames > 0 and sampled >= args.max_frames:
                        break
                frame_index += 1
    finally:
        capture.release()

    print(
        json.dumps(
            {
                "input": str(input_path),
                "output": str(output_path),
                "fps": fps,
                "total_frames": total_frames,
                "sample_rate": args.sample_rate,
                "sampled_frames": sampled,
                "manifest": str(manifest_path),
            },
            ensure_ascii=False,
            indent=2,
        )
    )


if __name__ == "__main__":
    main()
