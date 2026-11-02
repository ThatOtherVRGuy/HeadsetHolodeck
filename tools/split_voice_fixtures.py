#!/usr/bin/env python3
"""Split long voice recordings into the per-utterance fixtures the replay harness expects.

You record one WAV per *group* (a phrase said N times, with a pause between takes). This script finds
the pauses, trims each take, resamples to the 16 kHz mono the recogniser wants, and names the results
`<group>-NN.wav` so `VoiceFixtureReplay` can replay them and report per-clip accuracy.

Why a script rather than an audio editor: the split points have to be repeatable. The same audio must
produce the same fixtures before and after a model or parser change, otherwise "did it improve?" is
unanswerable — which is how the wake word reached 1-in-15 without anyone noticing.

Typical run:

    python tools/split_voice_fixtures.py --input C:\\Users\\daren\\Music\\voicetakes
    python tools/split_voice_fixtures.py --input ... --checklist      # just print what to record
    python tools/split_voice_fixtures.py --input ... --push           # also copy to the device

Requires numpy (bundled with this project's tooling). Stdlib `wave` handles the file I/O.
"""

from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
import wave
from dataclasses import dataclass
from typing import Iterable, List, Sequence, Tuple

try:
    import numpy as np
except ImportError:  # pragma: no cover - guidance, not logic
    sys.exit("numpy is required. Run with the bundled Python, or: pip install numpy")


TARGET_RATE = 16000


@dataclass(frozen=True)
class Group:
    """One recording file the owner produces, and how many takes it should contain."""

    name: str
    phrase: str
    takes: int
    why: str


# Order matters in the checklist: the wake word is by far the most valuable set.
GROUPS: Sequence[Group] = (
    Group("wake-computer", '"computer"', 10, "the 1-in-15 recall problem; record this set first"),
    Group("wake-inline-load", '"computer, load martian outpost"', 3, "wake + command in one breath"),
    Group("cmd-save-this-world", '"save this world"', 3, "local path, no cloud call"),
    Group("cmd-show-panorama", '"show panorama"', 3, "local path"),
    Group("cmd-my-worlds", '"my worlds"', 3, "the bare phrasing that used to be unparsed"),
    Group("cmd-load-castle", '"load castle dot glb"', 3, "spoken-filename parsing"),
    Group("cmd-open-camera", '"open camera"', 2, "local path"),
    Group("free-create-castle", '"create a world of a castle on a snowy mountain at sunset"', 2, "cloud path"),
    Group("free-add-rain", '"add the sound of rain"', 2, "cloud path"),
    Group("yes", '"yes"', 3, "confirmation handling"),
    Group("no", '"no"', 3, "confirmation handling"),
    Group("misparse-make-it-night", '"make it night"', 2, "known misparse to PlaceObject; must stay off the allowlist"),
    Group("noise-room-tone", "(say nothing, 10 seconds of room ambience)", 1, "false-wake control"),
)


def print_checklist() -> None:
    total = sum(g.takes for g in GROUPS if g.name != "noise-room-tone")
    print("Record one WAV per file below. Pause about a second between takes, and don't count aloud.\n")
    print(f"{'file':<28}{'say':<58}{'times':>6}")
    print("-" * 94)
    for group in GROUPS:
        print(f"{group.name + '.wav':<28}{group.phrase:<58}{group.takes:>6}")
    print("-" * 94)
    print(f"{total} utterances plus one silence clip. Name each file exactly as shown.\n")
    print("Any sample rate is fine; stereo is mixed down. Do not normalise or denoise.")


def read_wav(path: str) -> Tuple[np.ndarray, int]:
    """Return mono float32 samples in [-1, 1] plus the source sample rate."""
    with wave.open(path, "rb") as handle:
        channels = handle.getnchannels()
        width = handle.getsampwidth()
        rate = handle.getframerate()
        frames = handle.readframes(handle.getnframes())

    if width == 2:
        data = np.frombuffer(frames, dtype="<i2").astype(np.float32) / 32768.0
    elif width == 4:
        data = np.frombuffer(frames, dtype="<i4").astype(np.float32) / 2147483648.0
    elif width == 1:
        data = (np.frombuffer(frames, dtype=np.uint8).astype(np.float32) - 128.0) / 128.0
    else:
        raise ValueError(f"{os.path.basename(path)}: unsupported sample width {width * 8} bit")

    if channels > 1:
        data = data.reshape(-1, channels).mean(axis=1)

    return data.astype(np.float32), rate


def resample(samples: np.ndarray, source_rate: int, target_rate: int = TARGET_RATE) -> np.ndarray:
    if source_rate == target_rate or samples.size == 0:
        return samples

    duration = samples.size / float(source_rate)
    target_count = max(1, int(round(duration * target_rate)))
    source_positions = np.arange(samples.size, dtype=np.float64)
    target_positions = np.linspace(0.0, samples.size - 1, target_count)
    return np.interp(target_positions, source_positions, samples).astype(np.float32)


def frame_rms(samples: np.ndarray, frame: int, hop: int) -> np.ndarray:
    if samples.size < frame:
        return np.array([float(np.sqrt(np.mean(samples ** 2))) if samples.size else 0.0])

    count = 1 + (samples.size - frame) // hop
    powers = np.empty(count, dtype=np.float64)
    for index in range(count):
        chunk = samples[index * hop: index * hop + frame]
        powers[index] = float(np.mean(chunk ** 2))
    return np.sqrt(powers)


def db_to_amplitude(db: float) -> float:
    return float(10.0 ** (db / 20.0))


def find_speech_spans(
    samples: np.ndarray,
    rate: int,
    threshold_db: float,
    min_gap: float,
    min_clip: float,
) -> List[Tuple[int, int]]:
    """Return (start, end) sample spans, merged across pauses shorter than `min_gap`."""
    frame = int(0.025 * rate)
    hop = int(0.010 * rate)
    rms = frame_rms(samples, frame, hop)

    if rms.size == 0:
        return []

    # Threshold relative to the recording's own noise floor, but never below the absolute floor:
    # a quiet room and a noisy room then split at comparable relative levels.
    floor = float(np.percentile(rms, 20))
    threshold = max(db_to_amplitude(threshold_db), floor * db_to_amplitude(12.0))
    active = rms >= threshold

    if not active.any():
        return []

    min_gap_frames = max(1, int(min_gap / 0.010))
    spans: List[Tuple[int, int]] = []
    start_frame = None
    for index, is_active in enumerate(active):
        if is_active and start_frame is None:
            start_frame = index
        elif not is_active and start_frame is not None:
            spans.append((start_frame, index))
            start_frame = None
    if start_frame is not None:
        spans.append((start_frame, len(active)))

    merged: List[Tuple[int, int]] = []
    for span in spans:
        if merged and span[0] - merged[-1][1] < min_gap_frames:
            merged[-1] = (merged[-1][0], span[1])
        else:
            merged.append(list(span))  # type: ignore[arg-type]
    merged = [tuple(span) for span in merged]  # type: ignore[misc]

    # Convert frame indices to samples, pad generously, and drop clips that are too short to be a word.
    pad = int(0.05 * rate)
    result: List[Tuple[int, int]] = []
    for start_frame, end_frame in merged:
        start = max(0, start_frame * hop - pad)
        end = min(samples.size, end_frame * hop + pad)
        if (end - start) / rate >= min_clip:
            result.append((start, end))
    return result


def write_wav(path: str, samples: np.ndarray, rate: int = TARGET_RATE) -> None:
    clipped = np.clip(samples, -1.0, 1.0)
    pcm = (clipped * 32767.0).astype("<i2")
    with wave.open(path, "wb") as handle:
        handle.setnchannels(1)
        handle.setsampwidth(2)
        handle.setframerate(rate)
        handle.writeframes(pcm.tobytes())


def match_inputs(input_dir: str, group: Group) -> List[str]:
    """Accept `group.wav`, `group.WAV`, or any name containing the group id."""
    wanted = group.name.lower()
    matches = []
    for name in sorted(os.listdir(input_dir)):
        if not name.lower().endswith(".wav"):
            continue
        stem = os.path.splitext(name)[0].lower()
        if stem == wanted or wanted in stem:
            matches.append(os.path.join(input_dir, name))
    return matches


def find_adb() -> str | None:
    candidate = os.path.join(
        os.environ.get("LOCALAPPDATA", ""), "Android", "Sdk", "platform-tools", "adb.exe"
    )
    if os.path.isfile(candidate):
        return candidate
    return shutil.which("adb")


def push_to_device(fixture_dir: str, target: str) -> int:
    adb = find_adb()
    if not adb:
        print("adb not found; copy the folder to the device manually.")
        return 1

    print(f"\nPushing fixtures to {target} …")
    result = subprocess.run([adb, "-s", "emulator-5554", "push", fixture_dir, target])
    return result.returncode


def main(argv: Iterable[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--input", help="folder holding your long per-group recordings")
    parser.add_argument("--output", default="VoiceFixtures", help="where the split clips go (default: VoiceFixtures)")
    parser.add_argument("--checklist", action="store_true", help="print what to record and exit")
    parser.add_argument("--threshold-db", type=float, default=-35.0, help="silence floor in dBFS (default -35)")
    parser.add_argument("--min-gap", type=float, default=0.30, help="pauses shorter than this are not split points (s)")
    parser.add_argument("--min-clip", type=float, default=0.20, help="drop spans shorter than this (s)")
    parser.add_argument("--max-clip", type=float, default=8.0, help="warn when a clip exceeds this (s)")
    parser.add_argument("--push", action="store_true", help="adb push the result to the emulator")
    args = parser.parse_args(list(argv) if argv is not None else None)

    if args.checklist or not args.input:
        print_checklist()
        if not args.input:
            print("\nPass --input <folder> once the recordings exist.")
        return 0

    if not os.path.isdir(args.input):
        return _fail(f"input folder not found: {args.input}")

    os.makedirs(args.output, exist_ok=True)

    total_written = 0
    problems: List[str] = []

    for group in GROUPS:
        inputs = match_inputs(args.input, group)
        if not inputs:
            problems.append(f"{group.name}: no recording found")
            continue

        samples_all = []
        rate = TARGET_RATE
        for path in inputs:
            samples, rate = read_wav(path)
            samples_all.append(resample(samples, rate))
        samples = np.concatenate(samples_all) if samples_all else np.array([], dtype=np.float32)

        spans = find_speech_spans(samples, TARGET_RATE, args.threshold_db, args.min_gap, args.min_clip)
        if not spans:
            problems.append(f"{group.name}: no speech detected (raise --threshold-db, e.g. -45)")
            continue

        if len(spans) != group.takes:
            problems.append(f"{group.name}: found {len(spans)} takes, expected {group.takes}")

        for index, (start, end) in enumerate(spans, start=1):
            clip = samples[start:end]
            duration = clip.size / TARGET_RATE
            out_path = os.path.join(args.output, f"{group.name}-{index:02d}.wav")
            write_wav(out_path, clip)
            total_written += 1
            if duration > args.max_clip:
                problems.append(f"{os.path.basename(out_path)}: {duration:.1f}s is unusually long")

    print(f"\nWrote {total_written} clips to {os.path.abspath(args.output)}")
    if problems:
        print("\nNeeds attention:")
        for problem in problems:
            print(f"  - {problem}")
        print("\nRe-record those groups, or loosen --threshold-db / --min-gap and rerun.")
    else:
        print("Every group produced exactly the expected number of takes.")

    if args.push:
        target = "/sdcard/Android/data/com.QLC.HeadsetHolodeckPico/files/VoiceFixtures"
        return push_to_device(os.path.abspath(args.output), target)

    print("\nNext: push them, then send 'replay' through the debug command channel:")
    print("  adb push VoiceFixtures /sdcard/Android/data/com.QLC.HeadsetHolodeckPico/files/")
    return 0


def _fail(message: str) -> int:
    print(f"error: {message}")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
