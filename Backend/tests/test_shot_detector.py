from __future__ import annotations

import subprocess
import tempfile
import unittest
import wave
from pathlib import Path

import numpy as np

from app.vision.geometry import CameraModel
from app.vision.shot_detector import (
    AUDIO_SAMPLE_RATE,
    _onset_strength,
    _pick_peaks,
    _read_wav_mono,
    _sampling_warning,
    build_shot_stats,
    detect_shot_timestamps,
    extract_audio,
    find_ffmpeg,
)
from tests.test_metrics import make_frame


def write_click_track(
    path: Path,
    shot_times: list[float],
    duration: float = 3.0,
    rate: int = AUDIO_SAMPLE_RATE,
) -> None:
    """A quiet track with sharp decaying transients standing in for gunshots."""
    samples = np.random.default_rng(7).normal(0.0, 0.005, int(duration * rate))
    for shot_time in shot_times:
        start = int(shot_time * rate)
        length = int(0.08 * rate)
        envelope = np.exp(-np.linspace(0.0, 12.0, length))
        burst = np.random.default_rng(start).normal(0.0, 1.0, length) * envelope
        end = min(len(samples), start + length)
        samples[start:end] += burst[: end - start]

    peak = np.max(np.abs(samples)) or 1.0
    pcm = np.clip(samples / peak * 0.9, -1.0, 1.0)
    with wave.open(str(path), "wb") as handle:
        handle.setnchannels(1)
        handle.setsampwidth(2)
        handle.setframerate(rate)
        handle.writeframes((pcm * 32767).astype(np.int16).tobytes())


class OnsetDetectionTests(unittest.TestCase):
    def test_detects_isolated_shots_at_the_right_times(self) -> None:
        shot_times = [0.5, 1.2, 2.4]
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "clicks.wav"
            write_click_track(path, shot_times)
            decoded = _read_wav_mono(path)

        self.assertIsNotNone(decoded)
        assert decoded is not None
        samples, rate = decoded
        strength, hop = _onset_strength(samples, rate)
        detected = _pick_peaks(strength, hop)

        self.assertEqual(len(detected), len(shot_times))
        for expected, actual in zip(shot_times, detected):
            self.assertAlmostEqual(expected, actual, delta=0.03)

    def test_a_burst_is_not_collapsed_into_one_onset(self) -> None:
        """Rifle fire arrives about every 0.1 s and must stay separable."""
        shot_times = [0.5, 0.6, 0.7, 0.8]
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "burst.wav"
            write_click_track(path, shot_times)
            decoded = _read_wav_mono(path)

        assert decoded is not None
        samples, rate = decoded
        strength, hop = _onset_strength(samples, rate)
        detected = _pick_peaks(strength, hop)

        self.assertGreaterEqual(len(detected), 3)

    def test_silence_yields_no_shots(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "quiet.wav"
            write_click_track(path, [])
            decoded = _read_wav_mono(path)

        assert decoded is not None
        samples, rate = decoded
        strength, hop = _onset_strength(samples, rate)
        self.assertEqual(_pick_peaks(strength, hop), [])

    def test_short_input_does_not_crash(self) -> None:
        strength, hop = _onset_strength(np.zeros(4, dtype=np.float32), AUDIO_SAMPLE_RATE)
        self.assertEqual(strength.size, 0)
        self.assertGreater(hop, 0.0)


class ShotAlignmentTests(unittest.TestCase):
    def setUp(self) -> None:
        self.camera = CameraModel(width=1920, height=1080)

    def test_shots_take_the_deviation_of_the_nearest_frame(self) -> None:
        frames = [
            make_frame(0.0, target=(0.5, 0.40), camera=self.camera, index=0),
            make_frame(0.1, target=(0.5, 0.45), camera=self.camera, index=1),
            make_frame(0.2, target=(0.5, 0.49), camera=self.camera, index=2),
        ]

        stats = build_shot_stats(frames, [0.19], "audio_onset", "ok")

        self.assertEqual(stats.detected_shots, 1)
        self.assertEqual(stats.aligned_shots, 1)
        self.assertAlmostEqual(stats.shots[0].frame_timestamp, 0.2)
        expected = self.camera.angular_distance_deg((0.5, 0.49), (0.5, 0.5))
        self.assertAlmostEqual(stats.shots[0].offset_deg, expected, places=6)

    def test_a_shot_far_from_any_frame_stays_unaligned(self) -> None:
        """Attributing it to a distant frame would invent a measurement."""
        frames = [make_frame(0.0, target=(0.5, 0.4), camera=self.camera, index=0)]

        stats = build_shot_stats(frames, [9.0], "audio_onset", "ok")

        self.assertEqual(stats.detected_shots, 1)
        self.assertEqual(stats.aligned_shots, 0)
        self.assertIsNone(stats.shots[0].offset_deg)

    def test_reaction_time_is_measured_once_per_engagement(self) -> None:
        frames = [
            make_frame(0.0, target=None, camera=self.camera, index=0),
            make_frame(0.1, target=(0.6, 0.5), camera=self.camera, index=1),
            make_frame(0.2, target=(0.55, 0.5), camera=self.camera, index=2),
            make_frame(0.3, target=(0.51, 0.5), camera=self.camera, index=3),
        ]

        stats = build_shot_stats(frames, [0.25, 0.3], "audio_onset", "ok")

        # The engagement starts at 0.1, so the first shot reacts in 0.15 s.
        self.assertAlmostEqual(stats.shots[0].reaction_seconds, 0.15, places=6)
        # The follow-up round says nothing about reaction time.
        self.assertIsNone(stats.shots[1].reaction_seconds)
        self.assertAlmostEqual(stats.mean_reaction_seconds, 0.15, places=6)

    def test_overcorrection_needs_the_crosshair_to_cross_the_target(self) -> None:
        crossing = [
            make_frame(0.0, target=(0.65, 0.5), camera=self.camera, index=0),
            make_frame(0.1, target=(0.58, 0.5), camera=self.camera, index=1),
            # Overshot: the target is now on the other side of the crosshair.
            make_frame(0.2, target=(0.42, 0.5), camera=self.camera, index=2),
            make_frame(0.3, target=(0.49, 0.5), camera=self.camera, index=3),
        ]
        stats = build_shot_stats(crossing, [0.3], "audio_onset", "ok")
        self.assertTrue(stats.shots[0].overcorrected)
        self.assertEqual(stats.overcorrection_count, 1)

        approaching = [
            make_frame(0.0, target=(0.65, 0.5), camera=self.camera, index=0),
            make_frame(0.1, target=(0.60, 0.5), camera=self.camera, index=1),
            make_frame(0.2, target=(0.55, 0.5), camera=self.camera, index=2),
            make_frame(0.3, target=(0.52, 0.5), camera=self.camera, index=3),
        ]
        clean = build_shot_stats(approaching, [0.3], "audio_onset", "ok")
        self.assertFalse(clean.shots[0].overcorrected)

    def test_no_shots_reports_an_empty_but_valid_summary(self) -> None:
        stats = build_shot_stats([], [], "none", "no audio track")
        self.assertEqual(stats.detected_shots, 0)
        self.assertEqual(stats.source, "none")
        self.assertEqual(stats.deviation.count, 0)


class FfmpegIntegrationTests(unittest.TestCase):
    def setUp(self) -> None:
        if find_ffmpeg() is None:
            self.skipTest("ffmpeg is not available")

    def test_audio_is_extracted_from_a_generated_clip(self) -> None:
        executable = find_ffmpeg()
        assert executable is not None
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            video = root / "clip.mp4"
            subprocess.run(
                [
                    executable, "-hide_banner", "-loglevel", "error", "-y",
                    "-f", "lavfi", "-i", "color=c=black:s=320x240:d=2",
                    "-f", "lavfi", "-i", "sine=frequency=1000:duration=2",
                    "-shortest", str(video),
                ],
                check=True,
                capture_output=True,
                timeout=120,
            )

            extracted, message = extract_audio(video, root / "audio.wav")
            self.assertTrue(extracted, message)
            self.assertTrue((root / "audio.wav").is_file())

    def test_a_silent_video_reports_no_shots_without_failing(self) -> None:
        executable = find_ffmpeg()
        assert executable is not None
        with tempfile.TemporaryDirectory() as directory:
            video = Path(directory) / "silent.mp4"
            subprocess.run(
                [
                    executable, "-hide_banner", "-loglevel", "error", "-y",
                    "-f", "lavfi", "-i", "color=c=black:s=320x240:d=1",
                    str(video),
                ],
                check=True,
                capture_output=True,
                timeout=120,
            )

            timestamps, source, message = detect_shot_timestamps(video)

            self.assertEqual(timestamps, [])
            self.assertEqual(source, "none")
            self.assertTrue(message)


class SamplingWarningTests(unittest.TestCase):
    """A sparse sample must say so rather than quietly report low or zero.

    The first real recording was analysed at 2 fps and reported zero
    overcorrections, which reads as a clean result but was arithmetically
    unreachable: the 0.35 s window could not hold two samples.
    """

    def setUp(self) -> None:
        self.camera = CameraModel(width=1920, height=1080)

    def frames_at(self, fps: float, count: int = 20) -> list:
        return [
            make_frame(
                index / fps,
                target=(0.52, 0.5),
                camera=self.camera,
                index=index,
            )
            for index in range(count)
        ]

    def test_two_fps_is_reported_as_too_sparse(self) -> None:
        warning = _sampling_warning(self.frames_at(2.0), shot_count=50)
        self.assertIsNotNone(warning)
        assert warning is not None
        self.assertIn("can align", warning)
        self.assertIn("overcorrection cannot be detected", warning)

    def test_ten_fps_draws_no_warning(self) -> None:
        self.assertIsNone(_sampling_warning(self.frames_at(10.0), shot_count=50))

    def test_the_warning_reaches_the_reported_message(self) -> None:
        stats = build_shot_stats(
            self.frames_at(2.0),
            [1.05, 2.05],
            "audio_onset",
            "detected 2 shots",
        )
        self.assertIn("detected 2 shots", stats.message)
        self.assertIn("8 fps", stats.message)

    def test_no_warning_when_nothing_was_detected(self) -> None:
        self.assertIsNone(_sampling_warning(self.frames_at(2.0), shot_count=0))


class ReactionSampleTests(unittest.TestCase):
    """A mean of one engagement must not read as an average.

    On an aim trainer the targets never leave the screen, so the whole recording
    is one engagement and exactly one reaction time exists. The first real
    recording reported mean == median == 7.712 s from a single sample.
    """

    def setUp(self) -> None:
        self.camera = CameraModel(width=1920, height=1080)

    def test_a_permanently_visible_target_yields_one_reaction(self) -> None:
        frames = [
            make_frame(index / 10.0, target=(0.52, 0.5), camera=self.camera, index=index)
            for index in range(60)
        ]
        stats = build_shot_stats(frames, [1.0, 2.0, 3.0], "audio_onset", "detected 3 shots")
        self.assertEqual(stats.reaction_samples, 1)
        self.assertIn("rests on 1 engagement", stats.message)

    def test_repeated_engagements_are_not_flagged(self) -> None:
        frames = []
        for index in range(120):
            timestamp = index / 10.0
            # Target appears and disappears, giving several separate engagements.
            visible = (index // 10) % 2 == 0
            frames.append(
                make_frame(
                    timestamp,
                    target=(0.52, 0.5) if visible else None,
                    camera=self.camera,
                    index=index,
                )
            )
        shot_times = [0.35, 2.35, 4.35, 6.35]
        stats = build_shot_stats(frames, shot_times, "audio_onset", "detected 4 shots")
        self.assertGreaterEqual(stats.reaction_samples, 3)
        self.assertNotIn("rests on", stats.message)


if __name__ == "__main__":
    unittest.main()
