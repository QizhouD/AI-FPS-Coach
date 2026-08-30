"""Fire-moment detection from the recording's audio track.

The frame pipeline reads video through OpenCV, which discards audio entirely, so
nothing in it can tell which frame the player actually shot on. Without that,
the headline metric of the whole product, how far the crosshair sat from the
head at the instant of firing, cannot be computed at all.

Gunshots are near-ideal for onset detection: a sharp broadband transient against
a comparatively quiet background. This uses an energy-flux onset detector over a
mono downmix, which needs only numpy, rather than pulling in a full audio
analysis stack for one signal.
"""

from __future__ import annotations

import subprocess
import tempfile
import wave
from pathlib import Path
from statistics import fmean, median
from typing import Sequence

import numpy as np

from .metrics import bias_stats, deviation_stats
from .schemas import (
    BiasStats,
    DeviationStats,
    ShotMetric,
    ShotStats,
    VisionFrameResponse,
)

AUDIO_SAMPLE_RATE = 22050
WINDOW_SECONDS = 0.020
HOP_SECONDS = 0.005
# The fastest common CS2 fire rate is roughly 0.075 s between rounds; going
# below this starts splitting one report into several.
MIN_SHOT_INTERVAL_SECONDS = 0.06
# Onset strength must exceed the local baseline by this many robust deviations.
ONSET_THRESHOLD_FACTOR = 6.0
# Minimum jump in log energy for an onset to count, whatever the adaptive
# threshold says. Half the flux samples are clamped to zero, which drags the
# median and its deviation down far enough that stationary noise alone can clear
# a purely relative threshold. A gunshot raises the window energy by orders of
# magnitude, so its log rise runs to several units, while noise fluctuates by
# hundredths: an absolute floor separates the two cleanly.
#
# Tuned against synthetic transients. Worth re-checking against real CS2
# recordings, where footsteps and voice raise the floor.
MIN_ONSET_LOG_ENERGY_RISE = 0.5
# A shot is matched to a frame only if one was sampled this close to it.
MAX_ALIGNMENT_SECONDS = 0.12
# Window before a shot searched for a crosshair overshoot and return.
OVERCORRECTION_WINDOW_SECONDS = 0.35
OVERCORRECTION_MIN_DEG = 1.0
# Below this many engagements the reaction figures describe individual moments
# rather than a habit, and saying so beats reporting a mean of one.
MIN_REACTION_SAMPLES = 3


def find_ffmpeg() -> str | None:
    """Prefer the binary bundled in the virtualenv over a system install.

    Keeping the dependency inside the venv means a fresh clone works without
    touching the host, which matters because setup already asks a lot of the
    machine.
    """
    try:
        import imageio_ffmpeg

        return imageio_ffmpeg.get_ffmpeg_exe()
    except Exception:
        pass
    from shutil import which

    return which("ffmpeg")


def extract_audio(video_path: Path, destination: Path) -> tuple[bool, str]:
    executable = find_ffmpeg()
    if executable is None:
        return False, "ffmpeg is not available; install imageio-ffmpeg"

    command = [
        executable,
        "-hide_banner",
        "-loglevel",
        "error",
        "-nostdin",
        "-y",
        "-i",
        str(video_path),
        "-vn",
        "-ac",
        "1",
        "-ar",
        str(AUDIO_SAMPLE_RATE),
        "-f",
        "wav",
        str(destination),
    ]
    try:
        completed = subprocess.run(
            command,
            capture_output=True,
            text=True,
            timeout=600,
        )
    except (OSError, subprocess.SubprocessError) as exc:
        return False, f"ffmpeg failed to run: {exc}"

    if completed.returncode != 0 or not destination.is_file():
        detail = (completed.stderr or "").strip().splitlines()
        message = detail[-1] if detail else "unknown ffmpeg error"
        return False, f"audio extraction failed: {message}"
    if destination.stat().st_size == 0:
        return False, "the recording has no audio track"
    return True, "ok"


def _read_wav_mono(path: Path) -> tuple[np.ndarray, int] | None:
    try:
        with wave.open(str(path), "rb") as handle:
            channels = handle.getnchannels()
            width = handle.getsampwidth()
            rate = handle.getframerate()
            raw = handle.readframes(handle.getnframes())
    except (OSError, wave.Error):
        return None

    if width != 2:
        return None
    samples = np.frombuffer(raw, dtype=np.int16).astype(np.float32) / 32768.0
    if channels > 1:
        samples = samples.reshape(-1, channels).mean(axis=1)
    if samples.size == 0:
        return None
    return samples, rate


def _onset_strength(samples: np.ndarray, rate: int) -> tuple[np.ndarray, float]:
    """Positive frame-to-frame change in log energy, and the hop in seconds."""
    window = max(1, int(WINDOW_SECONDS * rate))
    hop = max(1, int(HOP_SECONDS * rate))
    if samples.size < window:
        return np.zeros(0, dtype=np.float32), hop / rate

    frame_count = 1 + (samples.size - window) // hop
    # A strided view avoids materialising an explicit copy per window, which for
    # a few minutes of audio would otherwise run to hundreds of megabytes.
    strides = (samples.strides[0] * hop, samples.strides[0])
    frames = np.lib.stride_tricks.as_strided(
        samples,
        shape=(frame_count, window),
        strides=strides,
        writeable=False,
    )
    energy = np.log1p((frames.astype(np.float32) ** 2).sum(axis=1))
    flux = np.diff(energy, prepend=energy[:1])
    return np.maximum(flux, 0.0), hop / rate


def _pick_peaks(strength: np.ndarray, hop_seconds: float) -> list[float]:
    if strength.size == 0:
        return []

    # A robust baseline, so that sustained loud passages such as a long spray do
    # not raise the threshold enough to swallow the individual reports.
    baseline = float(np.median(strength))
    deviation = float(np.median(np.abs(strength - baseline)))
    if deviation <= 0.0:
        deviation = float(strength.std()) or 1e-6
    threshold = max(
        MIN_ONSET_LOG_ENERGY_RISE,
        baseline + ONSET_THRESHOLD_FACTOR * deviation,
    )

    minimum_gap = max(1, int(MIN_SHOT_INTERVAL_SECONDS / hop_seconds))
    candidates = np.flatnonzero(strength > threshold)
    peaks: list[int] = []
    for index in candidates:
        if peaks and index - peaks[-1] < minimum_gap:
            # Keep whichever of the two is the stronger transient.
            if strength[index] > strength[peaks[-1]]:
                peaks[-1] = int(index)
            continue
        peaks.append(int(index))
    return [peak * hop_seconds for peak in peaks]


def detect_shot_timestamps(video_path: Path) -> tuple[list[float], str, str]:
    """Return fire timestamps in seconds, plus a source tag and a message."""
    with tempfile.TemporaryDirectory(prefix="fps-audio-") as directory:
        audio_path = Path(directory) / "track.wav"
        extracted, message = extract_audio(video_path, audio_path)
        if not extracted:
            return [], "none", message

        decoded = _read_wav_mono(audio_path)
        if decoded is None:
            return [], "none", "unable to decode the extracted audio track"
        samples, rate = decoded

    strength, hop_seconds = _onset_strength(samples, rate)
    timestamps = _pick_peaks(strength, hop_seconds)
    return timestamps, "audio_onset", f"detected {len(timestamps)} shots"


def _nearest_frame(
    frames: Sequence[VisionFrameResponse],
    timestamp: float,
) -> VisionFrameResponse | None:
    """Closest sampled frame to a shot, or None if the gap is too large.

    Frames are sampled at a fixed rate while shots land at arbitrary times, so
    an alignment that silently accepted any distance would attribute a shot to a
    frame from a completely different engagement.
    """
    if not frames:
        return None
    best: VisionFrameResponse | None = None
    best_gap = MAX_ALIGNMENT_SECONDS
    for frame in frames:
        gap = abs(frame.timestamp - timestamp)
        if gap <= best_gap:
            best_gap = gap
            best = frame
        elif frame.timestamp > timestamp + MAX_ALIGNMENT_SECONDS:
            break
    return best


def _engagement_starts(frames: Sequence[VisionFrameResponse]) -> list[tuple[float, float]]:
    """Spans during which a head stayed visible, as (start, end) timestamps."""
    spans: list[tuple[float, float]] = []
    start: float | None = None
    previous = 0.0
    for frame in frames:
        visible = frame.recommended_aim.target_id is not None
        if visible and start is None:
            start = frame.timestamp
        elif not visible and start is not None:
            spans.append((start, previous))
            start = None
        previous = frame.timestamp
    if start is not None:
        spans.append((start, previous))
    return spans


def _overcorrected(
    frames: Sequence[VisionFrameResponse],
    shot_time: float,
) -> bool:
    """Whether the crosshair crossed the target and swung back before firing.

    Detected as a sign change in the horizontal deviation during the approach,
    with both sides large enough to be a real overshoot rather than jitter
    around the target.
    """
    window = [
        frame.recommended_aim.offset_deg_x
        for frame in frames
        if shot_time - OVERCORRECTION_WINDOW_SECONDS <= frame.timestamp <= shot_time
        and frame.recommended_aim.offset_deg_x is not None
    ]
    significant = [value for value in window if abs(value) >= OVERCORRECTION_MIN_DEG]
    if len(significant) < 2:
        return False
    return any(
        earlier * later < 0.0
        for earlier, later in zip(significant, significant[1:])
    )


def _sampling_warning(
    frames: Sequence[VisionFrameResponse],
    shot_count: int,
) -> str | None:
    """Say so when the frame sampling is too sparse for these numbers to mean much.

    Shots align to a frame only within ``MAX_ALIGNMENT_SECONDS``, and an
    overcorrection needs at least two samples inside its window. Sample too
    coarsely and both simply come back low, or zero, with nothing to distinguish
    that from a player who never overcorrects. Reporting the limit is the whole
    point: a silent zero is worse than a missing number.
    """
    if len(frames) < 2 or shot_count == 0:
        return None
    gaps = [
        later.timestamp - earlier.timestamp
        for earlier, later in zip(frames, frames[1:])
        if later.timestamp > earlier.timestamp
    ]
    if not gaps:
        return None
    interval = median(gaps)

    notes: list[str] = []
    # Only a window of +/- the tolerance around each shot can ever contain a
    # sample, so this ratio caps the alignment rate however good detection is.
    reachable = min(1.0, 2.0 * MAX_ALIGNMENT_SECONDS / interval)
    if reachable < 0.95:
        notes.append(
            f"at {1.0 / interval:.1f} fps sampling at most {reachable:.0%} of shots "
            f"can align to a frame; resubmit at 8 fps or more"
        )
    if interval * 2.0 > OVERCORRECTION_WINDOW_SECONDS:
        notes.append(
            "overcorrection cannot be detected at this sample rate and is "
            "reported as zero regardless of the player"
        )
    return "; ".join(notes) if notes else None


def build_shot_stats(
    frames: Sequence[VisionFrameResponse],
    timestamps: Sequence[float],
    source: str,
    message: str,
) -> ShotStats:
    """Align detected shots to frames and summarise the section 8.2 metrics."""
    if not timestamps:
        return ShotStats(source=source, message=message)

    spans = _engagement_starts(frames)
    ordered_frames = sorted(frames, key=lambda frame: frame.timestamp)
    reacted_spans: set[float] = set()

    shots: list[ShotMetric] = []
    for timestamp in timestamps:
        frame = _nearest_frame(ordered_frames, timestamp)
        aim = frame.recommended_aim if frame is not None else None

        reaction: float | None = None
        for start, end in spans:
            if start <= timestamp <= end + MAX_ALIGNMENT_SECONDS:
                # Only the first shot of an engagement measures reaction time;
                # later rounds in the same spray say nothing about it.
                if start not in reacted_spans:
                    reacted_spans.add(start)
                    reaction = timestamp - start
                break

        shots.append(
            ShotMetric(
                timestamp=timestamp,
                frame_timestamp=frame.timestamp if frame is not None else None,
                offset_deg=aim.offset_deg if aim else None,
                offset_deg_x=aim.offset_deg_x if aim else None,
                offset_deg_y=aim.offset_deg_y if aim else None,
                target_id=aim.target_id if aim else None,
                reaction_seconds=reaction,
                overcorrected=_overcorrected(ordered_frames, timestamp),
            )
        )

    warning = _sampling_warning(ordered_frames, len(shots))
    if warning:
        message = f"{message}; {warning}"

    reaction_count = sum(1 for shot in shots if shot.reaction_seconds is not None)
    if 0 < reaction_count < MIN_REACTION_SAMPLES:
        message = (
            f"{message}; reaction time rests on {reaction_count} "
            f"engagement{'s' if reaction_count > 1 else ''} and is not an average. "
            f"A target that never leaves the screen gives only one"
        )

    aligned = [shot for shot in shots if shot.offset_deg is not None]
    deviations = [shot.offset_deg for shot in aligned if shot.offset_deg is not None]
    verticals = [shot.offset_deg_y for shot in aligned if shot.offset_deg_y is not None]
    horizontals = [shot.offset_deg_x for shot in aligned if shot.offset_deg_x is not None]
    reactions = [
        shot.reaction_seconds for shot in shots if shot.reaction_seconds is not None
    ]
    overcorrections = sum(1 for shot in shots if shot.overcorrected)

    return ShotStats(
        detected_shots=len(shots),
        aligned_shots=len(aligned),
        deviation=deviation_stats(deviations) if deviations else DeviationStats(),
        vertical_bias=bias_stats(verticals, "vertical") if verticals else BiasStats(),
        horizontal_bias=(
            bias_stats(horizontals, "horizontal") if horizontals else BiasStats()
        ),
        mean_reaction_seconds=fmean(reactions) if reactions else None,
        median_reaction_seconds=median(reactions) if reactions else None,
        reaction_samples=len(reactions),
        overcorrection_count=overcorrections,
        overcorrection_ratio=overcorrections / len(shots) if shots else 0.0,
        shots=shots,
        source=source,
        message=message,
    )


def analyze_shots(
    video_path: Path,
    frames: Sequence[VisionFrameResponse],
) -> ShotStats:
    timestamps, source, message = detect_shot_timestamps(video_path)
    return build_shot_stats(frames, timestamps, source, message)
