"""Replay harness -- feeds a corpus to any detector and scores it.

Per docs/GolfSimVision-Phase0-Harness-Specs.md "P0-1" section 5. Iterates clips, feeds
frames with their recorded timestamps (never wall-clock, never an assumed constant
interval), and collects whatever the detector emits, then produces the scorecard defined
there. Under ADR-0007 (precision over recall), false positives rank above false
negatives -- report both, but rank on false positives.
"""

from __future__ import annotations

import argparse
import importlib
import json
import statistics
from pathlib import Path
from typing import Optional, Protocol

import cv2

from corpus import frames_dir, load_clip, read_index


class Detector(Protocol):
    def reset(self) -> None: ...
    def push_frame(self, t: float, frame) -> Optional[object]: ...


def load_detector(dotted_path: str) -> Detector:
    """Import `dotted_path` and call its create_detector() factory."""
    module = importlib.import_module(dotted_path)
    return module.create_detector()


def iterate_clip_frames(corpus_root: Path, clip_id: str):
    """Yield (timestamp_s, grayscale_frame) pairs for a clip, in recorded order."""
    meta = load_clip(corpus_root, clip_id)
    frame_paths = sorted(frames_dir(corpus_root, clip_id).glob("*.png"))
    for t, path in zip(meta.frames.timestamps_s, frame_paths):
        frame = cv2.imread(str(path), cv2.IMREAD_GRAYSCALE)
        yield t, frame


def run_detector_on_clip(detector: Detector, corpus_root: Path, clip_id: str) -> list:
    """Reset the detector and feed it every frame of clip_id; return the measurements it fired."""
    detector.reset()
    measurements = []
    for t, frame in iterate_clip_frames(corpus_root, clip_id):
        result = detector.push_frame(t, frame)
        if result is not None:
            measurements.append(result)
    return measurements


def _pct_error(measured: float, truth: float) -> float:
    return abs(measured - truth) / truth * 100.0 if truth else 0.0


def _summarize_errors(errors: list[float]) -> dict:
    if not errors:
        return {"mean": 0.0, "median": 0.0, "p95": 0.0}
    sorted_errors = sorted(errors)
    idx95 = min(len(sorted_errors) - 1, int(round(0.95 * (len(sorted_errors) - 1))))
    return {
        "mean": statistics.mean(errors),
        "median": statistics.median(errors),
        "p95": sorted_errors[idx95],
    }


def _distribution(values: list[int]) -> dict:
    """min/max/mean plus a frequency histogram -- the scorecard's "distribution of
    points per measurement" (per the harness spec's scorecard table), not just a
    three-number summary."""
    if not values:
        return {"min": 0, "max": 0, "mean": 0.0, "counts": {}}
    counts: dict[int, int] = {}
    for v in values:
        counts[v] = counts.get(v, 0) + 1
    return {
        "min": min(values),
        "max": max(values),
        "mean": statistics.mean(values),
        "counts": {str(k): v for k, v in sorted(counts.items())},
    }


def score_corpus(detector: Detector, corpus_root: Path) -> dict:
    entries = read_index(corpus_root)

    true_positives = 0
    false_negatives = 0
    false_positives = 0
    double_fires = 0
    speed_errors: list[float] = []
    hla_errors: list[float] = []
    track_point_counts: list[int] = []
    rejection_counts: dict[str, int] = {}

    for entry in entries:
        clip_id = entry["clip_id"]
        meta = load_clip(corpus_root, clip_id)
        measurements = run_detector_on_clip(detector, corpus_root, clip_id)
        track_point_counts.extend(getattr(m, "track_points", 0) for m in measurements)

        for reason in getattr(detector, "rejections", []):
            rejection_counts[reason] = rejection_counts.get(reason, 0) + 1

        if meta.kind == "valid_putt":
            if len(measurements) == 1:
                true_positives += 1
                if meta.ground_truth.speed_mph is not None:
                    speed_errors.append(_pct_error(measurements[0].speed_mph, meta.ground_truth.speed_mph))
                if meta.ground_truth.hla_deg is not None:
                    hla_errors.append(abs(measurements[0].hla_deg - meta.ground_truth.hla_deg))
            elif len(measurements) == 0:
                false_negatives += 1
        else:
            if len(measurements) >= 1:
                false_positives += 1

        if len(measurements) > 1:
            double_fires += 1

    return {
        "clip_count": len(entries),
        "true_positives": true_positives,
        "false_negatives": false_negatives,
        "false_positives": false_positives,
        "double_fires": double_fires,
        "speed_error_pct": _summarize_errors(speed_errors),
        "hla_error_deg": _summarize_errors(hla_errors),
        "track_points": _distribution(track_point_counts),
        "rejections": rejection_counts,
    }


def main() -> None:
    parser = argparse.ArgumentParser(description="Replay a corpus against a detector and score it.")
    parser.add_argument("--detector", required=True, help="dotted module path, e.g. detectors.cam_putting_baseline")
    parser.add_argument("--corpus", default="corpus", help="path to corpus root")
    parser.add_argument("--report", help="path to write the scorecard as JSON")
    args = parser.parse_args()

    detector = load_detector(args.detector)
    scorecard = score_corpus(detector, Path(args.corpus))

    print(json.dumps(scorecard, indent=2))
    if args.report:
        Path(args.report).parent.mkdir(parents=True, exist_ok=True)
        Path(args.report).write_text(json.dumps(scorecard, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
