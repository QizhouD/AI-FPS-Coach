"""Pair a practice recording with its demo to measure detector accuracy.

Record a range session with OBS and with the console ``record`` command at the
same time. The demo then knows exactly where every target was on every tick, so
projecting those positions into the recording produces ground-truth head boxes
with no manual labelling, and comparing them against what the vision model found
gives its error in degrees.

The practice range is the easy case on purpose: one map, fixed lighting, no
smoke or flashes, few targets. Getting a trustworthy number here first is what
makes a number from real gameplay interpretable later.

Usage
-----
    python tools/pair_demo_video.py \\
        --demo media/range.dem \\
        --session <job_id> \\
        --player "YourName" \\
        --output reports/range-accuracy.json

Pass ``--video`` instead of ``--session`` to analyse the recording first.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from app.store import SessionStore  # noqa: E402
from app.vision.evaluation import (  # noqa: E402
    DEFAULT_MATCH_GATE_DEG,
    evaluate_session,
)
from app.vision.geometry import CS2_DEFAULT_FOV_DEG, CameraModel  # noqa: E402
from app.vision.projection import (  # noqa: E402
    CROUCHING_EYE_HEIGHT,
    STANDING_EYE_HEIGHT,
    ProjectedHead,
    angle_vectors,
    estimate_time_offset,
    eye_position,
    project_head,
)
from app.vision.schemas import VisionFrameResponse  # noqa: E402
from app.vision.shot_detector import detect_shot_timestamps  # noqa: E402

DEFAULT_TICK_RATE = 64.0

TICK_FIELDS = [
    "X",
    "Y",
    "Z",
    "pitch",
    "yaw",
    "health",
    "team_num",
    "is_alive",
]


@dataclass
class PlayerTick:
    name: str
    steamid: str
    team: int
    position: tuple[float, float, float]
    pitch: float
    yaw: float
    alive: bool
    ducked: bool


@dataclass
class DemoTracks:
    ticks: dict[int, list[PlayerTick]]
    fire_times: list[float]
    tick_rate: float

    def nearest_tick(self, demo_time: float) -> int | None:
        if not self.ticks:
            return None
        target = int(round(demo_time * self.tick_rate))
        if target in self.ticks:
            return target
        # Tick sampling can be sparse, so fall back to the closest one recorded.
        return min(self.ticks, key=lambda tick: abs(tick - target))


def _records(table: Any) -> list[dict[str, Any]]:
    if table is None:
        return []
    if hasattr(table, "to_dicts"):
        return table.to_dicts()
    if hasattr(table, "to_dict"):
        return list(table.to_dict(orient="records"))
    if isinstance(table, list):
        return table
    raise ValueError(f"Unsupported demoparser table type: {type(table).__name__}")


def _float(row: dict[str, Any], *keys: str, default: float = 0.0) -> float:
    for key in keys:
        value = row.get(key)
        if value is None:
            continue
        try:
            return float(value)
        except (TypeError, ValueError):
            continue
    return default


def load_demo(demo_path: Path, player: str, tick_rate: float) -> DemoTracks:
    from demoparser2 import DemoParser

    parser = DemoParser(str(demo_path))

    requested = list(TICK_FIELDS)
    try:
        rows = _records(parser.parse_ticks(requested))
    except Exception:
        # Field availability shifts between demo versions; retry with the
        # minimum needed to project anything at all.
        requested = ["X", "Y", "Z", "pitch", "yaw"]
        rows = _records(parser.parse_ticks(requested))

    ticks: dict[int, list[PlayerTick]] = {}
    for row in rows:
        tick = int(_float(row, "tick"))
        ticks.setdefault(tick, []).append(
            PlayerTick(
                name=str(row.get("name") or "").strip(),
                steamid=str(row.get("steamid") or "").strip(),
                team=int(_float(row, "team_num", default=0)),
                position=(
                    _float(row, "X"),
                    _float(row, "Y"),
                    _float(row, "Z"),
                ),
                pitch=_float(row, "pitch"),
                yaw=_float(row, "yaw"),
                alive=bool(row.get("is_alive", True)),
                ducked=bool(row.get("is_ducking", row.get("ducked", False))),
            )
        )

    fire_times: list[float] = []
    try:
        for row in _records(parser.parse_event("weapon_fire")):
            name = str(row.get("user_name") or "").strip()
            if player and name.lower() != player.lower():
                continue
            fire_times.append(_float(row, "tick") / tick_rate)
    except Exception:
        pass

    return DemoTracks(ticks=ticks, fire_times=sorted(fire_times), tick_rate=tick_rate)


def find_observer(players: Iterable[PlayerTick], player: str) -> PlayerTick | None:
    if not player:
        return None
    for entry in players:
        if entry.name.lower() == player.lower() or entry.steamid == player:
            return entry
    return None


def build_ground_truth(
    frames: list[VisionFrameResponse],
    tracks: DemoTracks,
    player: str,
    camera: CameraModel,
    offset_seconds: float,
) -> list[list[ProjectedHead]]:
    truth: list[list[ProjectedHead]] = []
    for frame in frames:
        demo_time = frame.timestamp + offset_seconds
        tick = tracks.nearest_tick(demo_time)
        if tick is None:
            truth.append([])
            continue

        players = tracks.ticks.get(tick, [])
        observer = find_observer(players, player)
        if observer is None:
            truth.append([])
            continue

        eye_height = CROUCHING_EYE_HEIGHT if observer.ducked else STANDING_EYE_HEIGHT
        eye = eye_position(observer.position, eye_height)
        basis = angle_vectors(observer.pitch, observer.yaw)

        heads: list[ProjectedHead] = []
        for other in players:
            if other is observer:
                continue
            if other.steamid and other.steamid == observer.steamid:
                continue
            if not other.alive:
                continue
            head = project_head(
                identifier=other.steamid or other.name or "unknown",
                team="CT" if other.team == 3 else "T",
                player_origin=other.position,
                eye=eye,
                basis=basis,
                camera=camera,
                eye_height=CROUCHING_EYE_HEIGHT if other.ducked else STANDING_EYE_HEIGHT,
            )
            if head is not None:
                heads.append(head)
        truth.append(heads)
    return truth


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--demo", required=True, type=Path)
    parser.add_argument(
        "--session",
        help="Job id of an already analysed recording, read from the session store.",
    )
    parser.add_argument(
        "--video",
        type=Path,
        help="Recording to align against, needed to detect shots for time alignment.",
    )
    parser.add_argument("--player", required=True, help="The recording player's name.")
    # Same environment variable the server reads, so the tool looks where the
    # sessions were actually written rather than beside the working directory.
    parser.add_argument(
        "--data-root",
        type=Path,
        default=Path(os.getenv("FPS_VISION_DATA_ROOT", "data")),
    )
    parser.add_argument(
        "--fov",
        type=float,
        default=float(os.getenv("FPS_VISION_FOV_DEG", CS2_DEFAULT_FOV_DEG)),
    )
    parser.add_argument("--tick-rate", type=float, default=DEFAULT_TICK_RATE)
    parser.add_argument("--gate-deg", type=float, default=DEFAULT_MATCH_GATE_DEG)
    parser.add_argument(
        "--offset",
        type=float,
        help="Seconds to add to a video timestamp to reach demo time. "
        "Estimated from gunshots when omitted.",
    )
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    if not args.demo.is_file():
        print(f"demo not found: {args.demo}", file=sys.stderr)
        return 2
    if not args.session:
        print("--session is required; analyse the recording first", file=sys.stderr)
        return 2

    store = SessionStore(args.data_root)
    frames = store.load_frames(args.session)
    metrics = store.load_metrics(args.session)
    if not frames:
        print(f"no stored frames for session {args.session}", file=sys.stderr)
        return 2

    camera = CameraModel(
        width=frames[0].frame_width,
        height=frames[0].frame_height,
        fov_deg=args.fov,
    )

    print(f"loading demo {args.demo.name}")
    tracks = load_demo(args.demo, args.player, args.tick_rate)
    print(f"  {len(tracks.ticks)} ticks, {len(tracks.fire_times)} shots by {args.player}")

    notes = ""
    alignment_score = 0.0
    if args.offset is not None:
        offset = args.offset
        notes = "time offset supplied by hand"
    else:
        video_shots: list[float] = []
        if args.video and args.video.is_file():
            video_shots, _, message = detect_shot_timestamps(args.video)
            print(f"  audio: {message}")
        elif metrics is not None:
            video_shots = [shot.timestamp for shot in metrics.shots.shots]

        offset, alignment_score = estimate_time_offset(video_shots, tracks.fire_times)
        notes = (
            f"time offset estimated from {len(video_shots)} audio shots against "
            f"{len(tracks.fire_times)} demo shots"
        )
        if alignment_score < 0.5:
            notes += (
                ". Alignment is weak; the ground truth is probably misaligned and "
                "the accuracy figures below should not be quoted."
            )

    print(f"  offset {offset:+.3f} s, alignment score {alignment_score:.0%}")

    truth = build_ground_truth(frames, tracks, args.player, camera, offset)
    report = evaluate_session(
        frames,
        truth,
        camera,
        gate_deg=args.gate_deg,
        time_offset_seconds=offset,
        alignment_score=alignment_score,
        notes=notes,
    )

    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("w", encoding="utf-8") as handle:
        json.dump(report.model_dump(mode="json"), handle, ensure_ascii=False, indent=2)

    print()
    print(f"frames            {report.frames}")
    print(f"ground-truth heads{report.total_truth:>6}")
    print(f"detections        {report.total_detections}")
    print(f"recall            {report.recall:.1%}")
    print(f"precision         {report.precision:.1%}")
    print(f"F1                {report.f1:.3f}")
    print(f"mean error        {report.mean_error_deg:.2f} deg")
    print(f"median error      {report.median_error_deg:.2f} deg")
    print(f"p90 error         {report.p90_error_deg:.2f} deg")
    print(f"mean IoU          {report.mean_iou:.3f}")
    for band, value in report.recall_by_distance.items():
        print(f"  recall {band:>10} units  {value:.1%}")
    print()
    print(f"written to {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
