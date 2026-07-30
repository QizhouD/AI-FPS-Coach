from __future__ import annotations

from collections import Counter
from pathlib import Path
from typing import Any
from uuid import uuid4

PLAYBACK_TICK_RATE = 64.0
PLAYBACK_SAMPLE_RATE = 2.0
MAX_PLAYBACK_FRAMES = 7200


def _records(table: Any) -> list[dict[str, Any]]:
    if table is None:
        return []
    if hasattr(table, "to_dicts"):
        return table.to_dicts()
    if hasattr(table, "to_dict"):
        data = table.to_dict(orient="records")
        return list(data)
    if isinstance(table, list):
        return table
    raise ValueError(f"Unsupported demoparser table type: {type(table).__name__}")


def _name(row: dict[str, Any], *keys: str) -> str:
    for key in keys:
        value = row.get(key)
        if value is not None:
            text = str(value).strip()
            if text and text.lower() not in {"nan", "none", "0"}:
                return text
    return ""


def _number(row: dict[str, Any], *keys: str) -> float:
    for key in keys:
        value = row.get(key)
        try:
            if value is not None:
                return float(value)
        except (TypeError, ValueError):
            continue
    return 0.0


def _sample_result() -> dict[str, Any]:
    return {
        "analysis_id": f"sample-{uuid4().hex[:8]}",
        "file_name": "sample_mirage.dem",
        "map_name": "de_mirage",
        "rounds": 24,
        "data_source": "sample",
        "player": {
            "name": "DemoPlayer",
            "kills": 18,
            "deaths": 17,
            "assists": 5,
            "headshots": 8,
            "headshot_percentage": 44.4,
            "kd_ratio": 1.06,
            "damage": 1896,
            "adr": 79.0,
            "opening_kills": 3,
            "opening_deaths": 6,
        },
        "insights": [
            {
                "severity": "warning",
                "title": "Opening duel efficiency needs improvement",
                "evidence": "3 opening kills and 6 opening deaths indicate low early-round conversion.",
                "action": "Avoid dry peeks and use teammate flashes or trade spacing.",
            },
            {
                "severity": "info",
                "title": "Headshot rate is within a useful range",
                "evidence": "8 of 18 kills were headshots, for a 44.4% headshot rate.",
                "action": "Maintain head-level pre-aim and improve counter-strafe stability.",
            },
            {
                "severity": "good",
                "title": "Damage output is stable",
                "evidence": "1,896 damage across 24 rounds produced 79.0 ADR.",
                "action": "Review high-damage rounds that did not convert into kills or trades.",
            },
        ],
        "playback": _sample_playback(),
    }


def _sample_playback() -> dict[str, Any]:
    frames: list[dict[str, Any]] = []
    duration_seconds = 24.0
    frame_count = int(duration_seconds * PLAYBACK_SAMPLE_RATE) + 1
    for frame_index in range(frame_count):
        time_seconds = frame_index / PLAYBACK_SAMPLE_RATE
        progress = time_seconds / duration_seconds
        players: list[dict[str, Any]] = []
        for player_index in range(10):
            team_num = 3 if player_index < 5 else 2
            team_index = player_index if team_num == 3 else player_index - 5
            direction = 1.0 if team_num == 3 else -1.0
            x = (0.18 + team_index * 0.12) if team_num == 3 else (0.82 - team_index * 0.12)
            y = 0.22 + team_index * 0.14
            players.append(
                {
                    "id": f"sample-{player_index}",
                    "name": f"Player {player_index + 1}",
                    "team": team_num,
                    "x": x + direction * progress * 0.28,
                    "y": y + (0.04 if player_index % 2 == 0 else -0.04) * progress,
                    "health": 100,
                    "alive": True,
                    "yaw": 0.0 if team_num == 3 else 180.0,
                }
            )
        frames.append(
            {
                "tick": int(time_seconds * PLAYBACK_TICK_RATE),
                "time": round(time_seconds, 3),
                "round": min(24, int(time_seconds // 8) + 1),
                "players": players,
            }
        )

    return {
        "duration": duration_seconds,
        "tick_rate": PLAYBACK_TICK_RATE,
        "sample_rate": PLAYBACK_SAMPLE_RATE,
        "coordinate_space": "normalized",
        "bounds": {"min_x": 0.0, "max_x": 1.0, "min_y": 0.0, "max_y": 1.0},
        "frames": frames,
    }


def sample_analysis() -> dict[str, Any]:
    return _sample_result()


def _build_playback(parser: Any) -> dict[str, Any]:
    try:
        tick_rows = _records(
            parser.parse_ticks(
                [
                    "X",
                    "Y",
                    "health",
                    "team_num",
                    "is_alive",
                    "yaw",
                    "total_rounds_played",
                ]
            )
        )
    except Exception:
        return {
            "duration": 0.0,
            "tick_rate": PLAYBACK_TICK_RATE,
            "sample_rate": PLAYBACK_SAMPLE_RATE,
            "coordinate_space": "world",
            "bounds": {"min_x": 0.0, "max_x": 1.0, "min_y": 0.0, "max_y": 1.0},
            "frames": [],
        }

    valid_rows = [
        row
        for row in tick_rows
        if _name(row, "name", "player_name")
        and any(key in row for key in ("X", "m_vecX"))
        and any(key in row for key in ("Y", "m_vecY"))
    ]
    if not valid_rows:
        return {
            "duration": 0.0,
            "tick_rate": PLAYBACK_TICK_RATE,
            "sample_rate": PLAYBACK_SAMPLE_RATE,
            "coordinate_space": "world",
            "bounds": {"min_x": 0.0, "max_x": 1.0, "min_y": 0.0, "max_y": 1.0},
            "frames": [],
        }

    valid_rows.sort(key=lambda row: int(_number(row, "tick")))
    first_tick = int(_number(valid_rows[0], "tick"))
    sample_interval = max(1, int(round(PLAYBACK_TICK_RATE / PLAYBACK_SAMPLE_RATE)))
    next_sample_tick = first_tick
    frames: list[dict[str, Any]] = []
    sampled_positions: list[tuple[float, float]] = []
    row_index = 0

    while row_index < len(valid_rows) and len(frames) < MAX_PLAYBACK_FRAMES:
        current_tick = int(_number(valid_rows[row_index], "tick"))
        tick_players: list[dict[str, Any]] = []
        while row_index < len(valid_rows):
            row = valid_rows[row_index]
            row_tick = int(_number(row, "tick"))
            if row_tick != current_tick:
                break
            if current_tick >= next_sample_tick:
                x = _number(row, "X", "m_vecX")
                y = _number(row, "Y", "m_vecY")
                sampled_positions.append((x, y))
                health = max(0, int(_number(row, "health")))
                steam_id = _name(row, "steamid", "player_steamid")
                name = _name(row, "name", "player_name")
                tick_players.append(
                    {
                        "id": steam_id or name,
                        "name": name,
                        "team": int(_number(row, "team_num")),
                        "x": x,
                        "y": y,
                        "health": health,
                        "alive": bool(row.get("is_alive", health > 0)),
                        "yaw": _number(row, "yaw"),
                    }
                )
            row_index += 1

        if current_tick < next_sample_tick:
            continue

        next_sample_tick = current_tick + sample_interval
        frames.append(
            {
                "tick": current_tick,
                "time": round((current_tick - first_tick) / PLAYBACK_TICK_RATE, 3),
                "round": max(
                    1,
                    int(
                        _number(
                            valid_rows[max(0, row_index - 1)],
                            "total_rounds_played",
                        )
                    )
                    + 1,
                ),
                "players": tick_players[:10],
            }
        )

    xs = [position[0] for position in sampled_positions]
    ys = [position[1] for position in sampled_positions]
    min_x, max_x = min(xs), max(xs)
    min_y, max_y = min(ys), max(ys)
    padding_x = max(64.0, (max_x - min_x) * 0.05)
    padding_y = max(64.0, (max_y - min_y) * 0.05)

    return {
        "duration": frames[-1]["time"] if frames else 0.0,
        "tick_rate": PLAYBACK_TICK_RATE,
        "sample_rate": PLAYBACK_SAMPLE_RATE,
        "coordinate_space": "world",
        "bounds": {
            "min_x": min_x - padding_x,
            "max_x": max_x + padding_x,
            "min_y": min_y - padding_y,
            "max_y": max_y + padding_y,
        },
        "frames": frames,
    }


def analyze_cs2_demo(path: str, original_name: str, target_player: str = "") -> dict[str, Any]:
    try:
        from demoparser2 import DemoParser
    except ImportError as exc:
        raise ValueError("demoparser2 is not installed. Run Backend/run.ps1 again.") from exc

    parser = DemoParser(path)
    try:
        deaths = _records(
            parser.parse_event(
                "player_death",
                other=["total_rounds_played", "is_warmup_period"],
            )
        )
    except Exception as exc:
        raise ValueError(f"Unable to parse the CS2 demo: {exc}") from exc

    deaths = [row for row in deaths if not bool(row.get("is_warmup_period", False))]
    if not deaths:
        raise ValueError("No competitive kill events were found in the demo.")

    try:
        hurts = _records(
            parser.parse_event(
                "player_hurt",
                other=["total_rounds_played", "is_warmup_period"],
            )
        )
        hurts = [row for row in hurts if not bool(row.get("is_warmup_period", False))]
    except Exception:
        hurts = []

    kills: Counter[str] = Counter()
    death_count: Counter[str] = Counter()
    assists: Counter[str] = Counter()
    headshots: Counter[str] = Counter()
    damage: Counter[str] = Counter()
    player_spellings: dict[str, str] = {}

    for row in deaths:
        attacker = _name(row, "attacker_name", "attacker")
        victim = _name(row, "user_name", "victim_name", "player_name")
        assister = _name(row, "assister_name", "assister")
        for current in (attacker, victim, assister):
            if current:
                player_spellings[current.casefold()] = current
        if attacker and attacker.casefold() != victim.casefold():
            kills[attacker.casefold()] += 1
            if bool(row.get("headshot", False)):
                headshots[attacker.casefold()] += 1
        if victim:
            death_count[victim.casefold()] += 1
        if assister:
            assists[assister.casefold()] += 1

    for row in hurts:
        attacker = _name(row, "attacker_name", "attacker")
        victim = _name(row, "user_name", "victim_name", "player_name")
        if attacker and attacker.casefold() != victim.casefold():
            amount = max(0, min(100, int(_number(row, "dmg_health", "damage", "health_damage"))))
            damage[attacker.casefold()] += amount

    if target_player.strip():
        key = target_player.strip().casefold()
        if key not in player_spellings:
            available = ", ".join(
                player_spellings[name] for name, _ in kills.most_common(10)
            )
            raise ValueError(f"Player '{target_player}' was not found. Available players: {available}")
    else:
        key = kills.most_common(1)[0][0]

    round_numbers = [
        int(_number(row, "total_rounds_played", "round"))
        for row in deaths
    ]
    rounds = max(round_numbers, default=0) + 1

    first_kills: dict[int, dict[str, Any]] = {}
    for row in sorted(deaths, key=lambda item: _number(item, "tick")):
        round_number = int(_number(row, "total_rounds_played", "round"))
        first_kills.setdefault(round_number, row)
    opening_kills = 0
    opening_deaths = 0
    for row in first_kills.values():
        attacker = _name(row, "attacker_name", "attacker").casefold()
        victim = _name(row, "user_name", "victim_name", "player_name").casefold()
        opening_kills += int(attacker == key)
        opening_deaths += int(victim == key)

    map_name = "unknown"
    if hasattr(parser, "parse_header"):
        try:
            header = parser.parse_header()
            if isinstance(header, dict):
                map_name = str(
                    header.get("map_name")
                    or header.get("map")
                    or header.get("mapname")
                    or "unknown"
                )
        except Exception:
            pass

    player_kills = kills[key]
    player_deaths = death_count[key]
    player_headshots = headshots[key]
    player_damage = damage[key]
    hs_percentage = round((player_headshots / player_kills * 100), 1) if player_kills else 0.0
    kd_ratio = round(player_kills / max(1, player_deaths), 2)
    adr = round(player_damage / max(1, rounds), 1)

    insights: list[dict[str, str]] = []
    if opening_deaths > opening_kills:
        insights.append(
            {
                "severity": "warning",
                "title": "Opening duels are unfavorable",
                "evidence": f"{opening_kills} opening kills and {opening_deaths} opening deaths.",
                "action": "Reduce unassisted peeks and maintain tradeable spacing.",
            }
        )
    else:
        insights.append(
            {
                "severity": "good",
                "title": "Opening contribution is stable",
                "evidence": f"{opening_kills} opening kills and {opening_deaths} opening deaths.",
                "action": "Keep effective opening routes and record the utility used before successful entries.",
            }
        )

    if hs_percentage < 35:
        insights.append(
            {
                "severity": "info",
                "title": "Improve first-shot placement",
                "evidence": f"Headshot percentage is {hs_percentage:.1f}%.",
                "action": "Review crosshair height 500 ms before rifle fights and train single-shot confirmation.",
            }
        )
    else:
        insights.append(
            {
                "severity": "good",
                "title": "Head-level control is effective",
                "evidence": f"{player_headshots} of {player_kills} kills were headshots.",
                "action": "Maintain pre-aim height and measure time to first-hit accuracy.",
            }
        )

    if adr and adr < 65:
        insights.append(
            {
                "severity": "warning",
                "title": "Round impact is below baseline",
                "evidence": f"Total damage: {player_damage}; ADR: {adr:.1f}.",
                "action": "Review early deaths and save rounds to create more convertible damage.",
            }
        )
    elif adr:
        insights.append(
            {
                "severity": "good",
                "title": "Damage output meets the baseline",
                "evidence": f"Total damage: {player_damage}; ADR: {adr:.1f}.",
                "action": "Review high-damage rounds that did not convert into kills or assists.",
            }
        )

    return {
        "analysis_id": uuid4().hex,
        "file_name": Path(original_name).name,
        "map_name": map_name,
        "rounds": rounds,
        "data_source": "demoparser2",
        "player": {
            "name": player_spellings.get(key, target_player or key),
            "kills": player_kills,
            "deaths": player_deaths,
            "assists": assists[key],
            "headshots": player_headshots,
            "headshot_percentage": hs_percentage,
            "kd_ratio": kd_ratio,
            "damage": player_damage,
            "adr": adr,
            "opening_kills": opening_kills,
            "opening_deaths": opening_deaths,
        },
        "insights": insights,
        "playback": _build_playback(parser),
    }
