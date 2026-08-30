from __future__ import annotations

import unittest

from app.vision.evaluation import evaluate_session, match_frame
from app.vision.geometry import CameraModel
from app.vision.projection import ProjectedHead
from app.vision.schemas import (
    ActualCrosshair,
    RecommendedAim,
    VisionEnemy,
    VisionFrameResponse,
)


def truth_head(
    identifier: str,
    center: tuple[float, float],
    half: float = 0.01,
    depth: float = 600.0,
    on_screen: bool = True,
) -> ProjectedHead:
    return ProjectedHead(
        identifier=identifier,
        team="T",
        x1=center[0] - half,
        y1=center[1] - half,
        x2=center[0] + half,
        y2=center[1] + half,
        depth=depth,
        on_screen=on_screen,
    )


def detection(
    identifier: str,
    center: tuple[float, float],
    half: float = 0.01,
    part: str = "head",
) -> VisionEnemy:
    return VisionEnemy(
        id=identifier,
        team="T",
        part=part,
        x1=center[0] - half,
        y1=center[1] - half,
        x2=center[0] + half,
        y2=center[1] + half,
        confidence=0.9,
    )


def frame(timestamp: float, enemies: list[VisionEnemy]) -> VisionFrameResponse:
    return VisionFrameResponse(
        timestamp=timestamp,
        frame_index=int(timestamp * 10),
        frame_width=1920,
        frame_height=1080,
        actual_crosshair=ActualCrosshair(visible=True),
        enemies=enemies,
        recommended_aim=RecommendedAim(),
        inference_ms=5.0,
    )


class MatchingTests(unittest.TestCase):
    def setUp(self) -> None:
        self.camera = CameraModel(width=1920, height=1080)

    def test_a_close_detection_matches_its_head(self) -> None:
        matches, missed, false_positives = match_frame(
            [detection("d0", (0.505, 0.5))],
            [truth_head("t0", (0.5, 0.5))],
            self.camera,
        )
        self.assertEqual(len(matches), 1)
        self.assertEqual(matches[0].truth_id, "t0")
        self.assertEqual(missed, [])
        self.assertEqual(false_positives, [])
        self.assertGreater(matches[0].error_deg, 0.0)

    def test_a_distant_detection_counts_as_both_a_miss_and_a_false_positive(self) -> None:
        matches, missed, false_positives = match_frame(
            [detection("d0", (0.9, 0.9))],
            [truth_head("t0", (0.5, 0.5))],
            self.camera,
        )
        self.assertEqual(matches, [])
        self.assertEqual(missed, ["t0"])
        self.assertEqual(false_positives, ["d0"])

    def test_each_head_takes_at_most_one_detection(self) -> None:
        """A duplicate box on one head is a false positive, not a second hit."""
        matches, missed, false_positives = match_frame(
            [detection("d0", (0.501, 0.5)), detection("d1", (0.503, 0.5))],
            [truth_head("t0", (0.5, 0.5))],
            self.camera,
        )
        self.assertEqual(len(matches), 1)
        self.assertEqual(matches[0].detection_id, "d0")
        self.assertEqual(false_positives, ["d1"])
        self.assertEqual(missed, [])

    def test_the_closest_pairing_wins_when_two_heads_are_near(self) -> None:
        matches, _, _ = match_frame(
            [detection("left", (0.40, 0.5)), detection("right", (0.60, 0.5))],
            [truth_head("t_left", (0.401, 0.5)), truth_head("t_right", (0.601, 0.5))],
            self.camera,
        )
        pairs = {match.detection_id: match.truth_id for match in matches}
        self.assertEqual(pairs, {"left": "t_left", "right": "t_right"})

    def test_body_boxes_are_ignored(self) -> None:
        """Ground truth is head positions, so only head detections may match."""
        matches, missed, false_positives = match_frame(
            [detection("body", (0.5, 0.5), part="body")],
            [truth_head("t0", (0.5, 0.5))],
            self.camera,
        )
        self.assertEqual(matches, [])
        self.assertEqual(missed, ["t0"])
        self.assertEqual(false_positives, [])


class EvaluationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.camera = CameraModel(width=1920, height=1080)

    def test_a_perfect_detector_scores_one(self) -> None:
        frames = [frame(index * 0.1, [detection("d", (0.5, 0.5))]) for index in range(5)]
        truth = [[truth_head("t", (0.5, 0.5))] for _ in range(5)]

        report = evaluate_session(frames, truth, self.camera)

        self.assertEqual(report.total_matched, 5)
        self.assertAlmostEqual(report.recall, 1.0)
        self.assertAlmostEqual(report.precision, 1.0)
        self.assertAlmostEqual(report.f1, 1.0)
        self.assertAlmostEqual(report.mean_error_deg, 0.0, places=9)
        self.assertAlmostEqual(report.mean_iou, 1.0, places=6)

    def test_a_detector_that_finds_nothing_scores_zero_recall(self) -> None:
        frames = [frame(index * 0.1, []) for index in range(4)]
        truth = [[truth_head("t", (0.5, 0.5))] for _ in range(4)]

        report = evaluate_session(frames, truth, self.camera)

        self.assertEqual(report.total_missed, 4)
        self.assertAlmostEqual(report.recall, 0.0)
        self.assertAlmostEqual(report.precision, 0.0)

    def test_offscreen_heads_are_not_counted_against_the_detector(self) -> None:
        """A head behind the player cannot be found and must not lower recall."""
        frames = [frame(0.0, [])]
        truth = [[truth_head("hidden", (1.4, 0.5), on_screen=False)]]

        report = evaluate_session(frames, truth, self.camera)

        self.assertEqual(report.total_truth, 0)
        self.assertEqual(report.total_missed, 0)

    def test_recall_is_broken_down_by_distance(self) -> None:
        frames = [
            frame(0.0, [detection("near", (0.5, 0.5))]),
            frame(0.1, []),
        ]
        truth = [
            [truth_head("t_near", (0.5, 0.5), depth=300.0)],
            [truth_head("t_far", (0.5, 0.5), depth=1500.0)],
        ]

        report = evaluate_session(frames, truth, self.camera)

        self.assertAlmostEqual(report.recall_by_distance["0-500"], 1.0)
        self.assertAlmostEqual(report.recall_by_distance["1000-2000"], 0.0)

    def test_a_systematic_low_box_shows_as_a_signed_vertical_error(self) -> None:
        """A consistent offset in the detector would bias every deviation reading."""
        frames = [frame(index * 0.1, [detection("d", (0.5, 0.52))]) for index in range(6)]
        truth = [[truth_head("t", (0.5, 0.5))] for _ in range(6)]

        report = evaluate_session(frames, truth, self.camera)

        self.assertEqual(report.total_matched, 6)
        # The detection sits below the true head, so reaching it means moving down.
        self.assertLess(report.mean_vertical_error_deg, 0.0)
        self.assertGreater(report.mean_error_deg, 0.0)

    def test_mismatched_lengths_are_rejected(self) -> None:
        with self.assertRaises(ValueError):
            evaluate_session([frame(0.0, [])], [], self.camera)

    def test_alignment_metadata_is_carried_through(self) -> None:
        report = evaluate_session(
            [frame(0.0, [])],
            [[]],
            self.camera,
            time_offset_seconds=1.25,
            alignment_score=0.8,
            notes="estimated from gunshots",
        )
        self.assertAlmostEqual(report.time_offset_seconds, 1.25)
        self.assertAlmostEqual(report.alignment_score, 0.8)
        self.assertIn("gunshots", report.notes)


if __name__ == "__main__":
    unittest.main()
