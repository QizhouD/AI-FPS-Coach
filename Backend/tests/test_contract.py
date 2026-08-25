import asyncio
import io
import sys
import tempfile
import types
import unittest
from unittest.mock import patch

from fastapi import UploadFile

from app.demo_analyzer import analyze_cs2_demo
from app.main import analyze_demo_sample, analyze_frame, health


class ContractTests(unittest.TestCase):
    def test_health(self) -> None:
        payload = health()
        self.assertEqual(payload["status"], "ok")
        self.assertIn("vision", payload)
        self.assertIn("device", payload["vision"])
        self.assertIn("enemy_model", payload["vision"])
        self.assertIn("cuda_available", payload["vision"])

    def test_frame_analysis_contract(self) -> None:
        upload = UploadFile(filename="frame.jpg", file=io.BytesIO(b"fake-jpeg-smoke-test"))
        result = asyncio.run(
            analyze_frame(frame=upload, game="cs2", session_id="unit-test")
        )

        self.assertEqual(result.session_id, "unit-test")
        self.assertGreaterEqual(result.scores.aim, 0)
        self.assertLessEqual(result.scores.aim, 100)
        self.assertTrue(result.tip.title)
        self.assertTrue(result.tip.action)

    def test_demo_sample_contract(self) -> None:
        result = analyze_demo_sample()
        self.assertEqual(result["data_source"], "sample")
        self.assertGreater(result["rounds"], 0)
        self.assertGreaterEqual(len(result["insights"]), 2)
        self.assertGreater(result["playback"]["duration"], 0)
        self.assertGreater(len(result["playback"]["frames"]), 10)

    def test_demo_analyzer_aggregates_player_events(self) -> None:
        deaths = [
            {
                "attacker_name": "Ace",
                "user_name": "Enemy1",
                "assister_name": "",
                "headshot": True,
                "total_rounds_played": 0,
                "tick": 100,
                "is_warmup_period": False,
            },
            {
                "attacker_name": "Enemy2",
                "user_name": "Ace",
                "assister_name": "",
                "headshot": False,
                "total_rounds_played": 1,
                "tick": 200,
                "is_warmup_period": False,
            },
            {
                "attacker_name": "Ace",
                "user_name": "Enemy2",
                "assister_name": "Teammate",
                "headshot": False,
                "total_rounds_played": 1,
                "tick": 220,
                "is_warmup_period": False,
            },
        ]
        hurts = [
            {
                "attacker_name": "Ace",
                "user_name": "Enemy1",
                "dmg_health": 100,
                "is_warmup_period": False,
            },
            {
                "attacker_name": "Ace",
                "user_name": "Enemy2",
                "dmg_health": 80,
                "is_warmup_period": False,
            },
        ]

        class FakeParser:
            def __init__(self, _: str) -> None:
                pass

            def parse_event(self, event: str, **_: object) -> list[dict]:
                return deaths if event == "player_death" else hurts

            def parse_header(self) -> dict[str, str]:
                return {"map_name": "de_test"}

            def parse_ticks(self, _: list[str]) -> list[dict]:
                return [
                    {
                        "tick": tick,
                        "name": name,
                        "steamid": steamid,
                        "X": x,
                        "Y": y,
                        "health": 100,
                        "team_num": team,
                        "is_alive": True,
                        "yaw": 0.0,
                        "total_rounds_played": 1,
                    }
                    for tick in (100, 132)
                    for name, steamid, team, x, y in (
                        ("Ace", "1", 3, 100.0 + tick, 200.0),
                        ("Enemy2", "2", 2, 500.0 - tick, 600.0),
                    )
                ]

        fake_module = types.SimpleNamespace(DemoParser=FakeParser)
        with tempfile.NamedTemporaryFile(suffix=".dem") as handle:
            with patch.dict(sys.modules, {"demoparser2": fake_module}):
                result = analyze_cs2_demo(handle.name, "match.dem", "Ace")

        self.assertEqual(result["map_name"], "de_test")
        self.assertEqual(result["rounds"], 2)
        self.assertEqual(result["player"]["kills"], 2)
        self.assertEqual(result["player"]["deaths"], 1)
        self.assertEqual(result["player"]["damage"], 180)
        self.assertEqual(result["player"]["opening_kills"], 1)
        self.assertEqual(result["player"]["opening_deaths"], 1)
        self.assertEqual(len(result["playback"]["frames"]), 2)
        self.assertEqual(len(result["playback"]["frames"][0]["players"]), 2)


if __name__ == "__main__":
    unittest.main()
