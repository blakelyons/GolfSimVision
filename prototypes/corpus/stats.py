"""Corpus summary + frame-interval jitter report.

Per docs/GolfSimVision-Phase0-Harness-Specs.md "P0-1" section 2: across the corpus,
report median/p95/max frame interval, standard deviation, and a count of gaps
(intervals > 1.5 x median), plus a histogram. This is a first-class Phase 0 finding,
not just a debugging aid -- the eventual write-up goes in docs/CAMERA_CAPABILITIES.md.
"""

from __future__ import annotations

import argparse
import statistics
from pathlib import Path

from corpus import load_clip, read_index


def intervals_ms(timestamps_s: list[float]) -> list[float]:
    """Per-frame arrival intervals in milliseconds, from consecutive timestamps."""
    return [(b - a) * 1000.0 for a, b in zip(timestamps_s, timestamps_s[1:])]


def jitter_report(intervals: list[float]) -> dict:
    """median/p95/max/stdev interval (ms) and a count of gaps (> 1.5x median)."""
    if not intervals:
        return {"median": 0.0, "p95": 0.0, "max": 0.0, "stdev": 0.0, "gap_count": 0}
    sorted_intervals = sorted(intervals)
    median = statistics.median(sorted_intervals)
    p95 = percentile(sorted_intervals, 0.95)
    gap_threshold = 1.5 * median
    return {
        "median": median,
        "p95": p95,
        "max": max(intervals),
        "stdev": statistics.pstdev(intervals) if len(intervals) > 1 else 0.0,
        "gap_count": sum(1 for i in intervals if i > gap_threshold),
    }


def percentile(sorted_values: list[float], fraction: float) -> float:
    """Nearest-rank percentile over an already-sorted list."""
    if not sorted_values:
        return 0.0
    idx = min(len(sorted_values) - 1, int(round(fraction * (len(sorted_values) - 1))))
    return sorted_values[idx]


def histogram(intervals: list[float], bucket_ms: float = 1.0, width: int = 40) -> str:
    """Text histogram of interval durations, one row per bucket_ms-wide bucket."""
    if not intervals:
        return "(no intervals)"
    lo = int(min(intervals) // bucket_ms)
    hi = int(max(intervals) // bucket_ms)
    counts = {b: 0 for b in range(lo, hi + 1)}
    for i in intervals:
        counts[int(i // bucket_ms)] += 1
    max_count = max(counts.values())
    lines = []
    for bucket in range(lo, hi + 1):
        count = counts[bucket]
        bar_len = round((count / max_count) * width) if max_count else 0
        label = f"{bucket * bucket_ms:6.1f}-{(bucket + 1) * bucket_ms:.1f}ms"
        lines.append(f"{label} | {'#' * bar_len} {count}")
    return "\n".join(lines)


def all_intervals_ms(corpus_root: Path) -> list[float]:
    """Every frame interval across every clip in the corpus, in milliseconds."""
    all_intervals: list[float] = []
    for entry in read_index(corpus_root):
        meta = load_clip(corpus_root, entry["clip_id"])
        all_intervals.extend(intervals_ms(meta.frames.timestamps_s))
    return all_intervals


def corpus_summary(corpus_root: Path) -> dict:
    """Clip counts by kind/labelling status, plus a corpus-wide jitter report."""
    entries = read_index(corpus_root)
    return {
        "clip_count": len(entries),
        "by_kind": _count_by(entries, "kind"),
        "with_ground_truth": sum(1 for e in entries if e.get("has_ground_truth")),
        "with_tier2_labels": sum(1 for e in entries if e.get("labels_tier2")),
        "jitter": jitter_report(all_intervals_ms(corpus_root)),
    }


def _count_by(entries: list[dict], key: str) -> dict:
    counts: dict[str, int] = {}
    for e in entries:
        counts[e[key]] = counts.get(e[key], 0) + 1
    return counts


def main() -> None:
    parser = argparse.ArgumentParser(description="Corpus summary and jitter report.")
    parser.add_argument("--corpus", default="corpus", help="path to corpus root")
    parser.add_argument(
        "--histogram", action="store_true", help="also print a jitter histogram"
    )
    args = parser.parse_args()

    root = Path(args.corpus)
    summary = corpus_summary(root)

    print(f"Clips: {summary['clip_count']}")
    for kind, count in summary["by_kind"].items():
        print(f"  {kind}: {count}")
    print(f"With ground truth: {summary['with_ground_truth']}")
    print(f"With tier-2 labels: {summary['with_tier2_labels']}")

    j = summary["jitter"]
    print("\nFrame interval (ms):")
    print(f"  median={j['median']:.2f}  p95={j['p95']:.2f}  max={j['max']:.2f}")
    print(f"  stdev={j['stdev']:.2f}  gaps(>1.5x median)={j['gap_count']}")

    if args.histogram:
        print("\nHistogram:")
        print(histogram(all_intervals_ms(root)))


if __name__ == "__main__":
    main()
