"""Score several sets of weights on one paired recording, for a like-for-like baseline.

The starting weights are a CSGO player detector. Whether they are good enough on
CS2 footage, and whether fine-tuning or replacing them is the better use of
effort, is a question about measured accuracy rather than taste. This runs each
candidate over the same video, evaluates every one against the same
demo-projected ground truth, and prints the comparison.

Holding the video, the demo, the FOV, the sample rate and the matching gate
fixed across candidates is the point: the only thing that varies is the weights,
so the difference in the table is attributable to them.

Usage
-----
    python tools/compare_models.py \\
        --video media/range.mp4 \\
        --demo media/range.dem \\
        --player "YourName" \\
        --model baseline=models/yolov8m-csgo.pt \\
        --model finetuned=models/yolov8m-cs2-finetuned.pt \\
        --output reports/baseline-comparison.json
"""

from __future__ import annotations

import argparse
import json
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from app.vision.evaluation import (  # noqa: E402
    DEFAULT_MATCH_GATE_DEG,
    EvaluationReport,
    evaluate_session,
)
from app.vision.geometry import CS2_DEFAULT_FOV_DEG, CameraModel  # noqa: E402
from app.vision.projection import estimate_time_offset  # noqa: E402
from app.vision.shot_detector import detect_shot_timestamps  # noqa: E402
from app.vision.worker import VisionInferenceEngine, VisionJobManager  # noqa: E402
from tools.pair_demo_video import (  # noqa: E402
    DEFAULT_TICK_RATE,
    build_ground_truth,
    load_demo,
)


def parse_model(argument: str) -> tuple[str, Path]:
    if "=" not in argument:
        raise argparse.ArgumentTypeError(
            "expected NAME=PATH, for example baseline=models/yolov8m-csgo.pt"
        )
    name, _, path = argument.partition("=")
    return name.strip(), Path(path.strip())


def analyze(
    video: Path,
    weights: Path,
    device: str,
    confidence: float,
    sample_rate: float,
    fov_deg: float,
) -> list:
    """Run one set of weights over the video and return the analysed frames."""
    engine = VisionInferenceEngine(
        enemy_model_path=str(weights),
        crosshair_baseline=True,
        confidence=confidence,
        device=device,
        fov_deg=fov_deg,
    )
    if not engine.enemy_detector.available:
        raise RuntimeError(engine.enemy_detector.status)

    jobs = VisionJobManager(engine, str(video.parent))
    try:
        job_id = jobs.submit(
            str(video),
            f"compare-{weights.stem}",
            sample_rate,
            fov_deg=fov_deg,
            detect_shots=False,
        )
        while True:
            state = jobs.get(job_id, results_from=0)
            if state is None:
                raise RuntimeError("the job disappeared")
            if state.status == "failed":
                raise RuntimeError(state.error or "analysis failed")
            if state.status == "completed":
                return state.results or []
            time.sleep(0.25)
    finally:
        jobs.shutdown()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--video", required=True, type=Path)
    parser.add_argument("--demo", required=True, type=Path)
    parser.add_argument("--player", required=True)
    parser.add_argument(
        "--model",
        required=True,
        action="append",
        type=parse_model,
        metavar="NAME=PATH",
        help="Repeat once per candidate.",
    )
    parser.add_argument("--device", default="cuda")
    parser.add_argument("--confidence", type=float, default=0.25)
    parser.add_argument("--sample-rate", type=float, default=5.0)
    parser.add_argument("--fov", type=float, default=CS2_DEFAULT_FOV_DEG)
    parser.add_argument("--tick-rate", type=float, default=DEFAULT_TICK_RATE)
    parser.add_argument("--gate-deg", type=float, default=DEFAULT_MATCH_GATE_DEG)
    parser.add_argument("--offset", type=float)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()

    for path in (args.video, args.demo):
        if not path.is_file():
            print(f"not found: {path}", file=sys.stderr)
            return 2

    print(f"loading demo {args.demo.name}")
    tracks = load_demo(args.demo, args.player, args.tick_rate)
    print(f"  {len(tracks.ticks)} ticks, {len(tracks.fire_times)} shots by {args.player}")

    if args.offset is not None:
        offset, alignment = args.offset, 1.0
        print(f"  offset {offset:+.3f} s supplied by hand")
    else:
        shots, _, message = detect_shot_timestamps(args.video)
        print(f"  audio: {message}")
        offset, alignment = estimate_time_offset(shots, tracks.fire_times)
        print(f"  offset {offset:+.3f} s, alignment score {alignment:.0%}")
        if alignment < 0.5:
            print(
                "  WARNING: weak alignment. The ground truth is probably "
                "misaligned and this comparison will not mean much.",
                file=sys.stderr,
            )

    reports: dict[str, EvaluationReport] = {}
    for name, weights in args.model:
        if not weights.is_file():
            print(f"skipping {name}: {weights} not found", file=sys.stderr)
            continue

        print(f"\nanalysing with {name} ({weights.name})")
        started = time.perf_counter()
        try:
            frames = analyze(
                args.video,
                weights,
                args.device,
                args.confidence,
                args.sample_rate,
                args.fov,
            )
        except Exception as exc:
            print(f"  failed: {exc}", file=sys.stderr)
            continue
        elapsed = time.perf_counter() - started
        print(f"  {len(frames)} frames in {elapsed:.1f} s")

        if not frames:
            continue

        camera = CameraModel(
            width=frames[0].frame_width,
            height=frames[0].frame_height,
            fov_deg=args.fov,
        )
        truth = build_ground_truth(frames, tracks, args.player, camera, offset)
        reports[name] = evaluate_session(
            frames,
            truth,
            camera,
            gate_deg=args.gate_deg,
            time_offset_seconds=offset,
            alignment_score=alignment,
            notes=f"weights={weights.name} confidence={args.confidence}",
        )

    if not reports:
        print("nothing was evaluated", file=sys.stderr)
        return 1

    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("w", encoding="utf-8") as handle:
        json.dump(
            {
                "video": args.video.name,
                "demo": args.demo.name,
                "player": args.player,
                "fov_deg": args.fov,
                "confidence": args.confidence,
                "sample_rate": args.sample_rate,
                "match_gate_deg": args.gate_deg,
                "time_offset_seconds": offset,
                "alignment_score": alignment,
                "models": {
                    name: report.model_dump(mode="json")
                    for name, report in reports.items()
                },
            },
            handle,
            ensure_ascii=False,
            indent=2,
        )

    header = (
        f"{'model':<18}{'recall':>9}{'precision':>11}{'F1':>8}"
        f"{'mean err':>10}{'p90 err':>10}{'IoU':>7}"
    )
    print()
    print(header)
    print("-" * len(header))
    for name, report in reports.items():
        print(
            f"{name:<18}{report.recall:>8.1%}{report.precision:>11.1%}"
            f"{report.f1:>8.3f}{report.mean_error_deg:>9.2f}°"
            f"{report.p90_error_deg:>9.2f}°{report.mean_iou:>7.3f}"
        )
    print()
    print(f"written to {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
