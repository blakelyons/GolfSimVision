import shutil
import tempfile
import unittest
from pathlib import Path

from corpus import (
    CameraProps,
    ClipMetadata,
    Frames,
    GroundTruth,
    Timing,
    append_index_entry,
    save_clip,
)
from stats import corpus_summary, histogram, intervals_ms, jitter_report, percentile


class TestIntervalsMs(unittest.TestCase):
    def test_converts_consecutive_gaps_to_milliseconds(self):
        timestamps = [0.0, 0.010, 0.021, 0.030]

        result = intervals_ms(timestamps)

        self.assertEqual(len(result), 3)
        self.assertAlmostEqual(result[0], 10.0)
        self.assertAlmostEqual(result[1], 11.0)
        self.assertAlmostEqual(result[2], 9.0)

    def test_single_timestamp_has_no_intervals(self):
        self.assertEqual(intervals_ms([0.0]), [])


class TestPercentile(unittest.TestCase):
    def test_p95_of_sorted_values(self):
        values = list(range(1, 101))  # 1..100

        self.assertEqual(percentile(values, 0.95), 95)

    def test_empty_list_returns_zero(self):
        self.assertEqual(percentile([], 0.95), 0.0)


class TestJitterReport(unittest.TestCase):
    def test_empty_intervals_returns_zeroed_report(self):
        report = jitter_report([])

        self.assertEqual(report["median"], 0.0)
        self.assertEqual(report["gap_count"], 0)

    def test_steady_intervals_have_no_gaps(self):
        intervals = [8.3] * 100

        report = jitter_report(intervals)

        self.assertAlmostEqual(report["median"], 8.3)
        self.assertEqual(report["gap_count"], 0)
        self.assertAlmostEqual(report["stdev"], 0.0)

    def test_gap_count_flags_intervals_over_1_5x_median(self):
        # median is 8.3; 1.5x median = 12.45, so only the 25ms outlier counts.
        intervals = [8.3] * 10 + [25.0]

        report = jitter_report(intervals)

        self.assertEqual(report["gap_count"], 1)
        self.assertAlmostEqual(report["max"], 25.0)


class TestHistogram(unittest.TestCase):
    def test_empty_intervals(self):
        self.assertEqual(histogram([]), "(no intervals)")

    def test_buckets_and_counts_all_values(self):
        intervals = [8.1, 8.2, 8.9, 9.5]

        result = histogram(intervals, bucket_ms=1.0)

        # 8.1/8.2/8.9 fall in the 8-9ms bucket, 9.5 in the 9-10ms bucket.
        lines = result.splitlines()
        self.assertEqual(len(lines), 2)
        self.assertIn("3", lines[0])
        self.assertIn("1", lines[1])


def make_meta(clip_id, kind, timestamps, with_ground_truth=False, tier2=False):
    return ClipMetadata(
        clip_id=clip_id,
        created_utc="2026-09-16T00:00:00Z",
        kind=kind,
        camera=CameraProps(requested={}, actual={}),
        frames=Frames(count=len(timestamps), first_index=0, timestamps_s=timestamps),
        timing=Timing(measured_fps=120.0, interval_ms={}, gap_count=0),
        ground_truth=GroundTruth(method="two_gate_manual") if with_ground_truth else GroundTruth(),
        labels_tier2=tier2,
    )


class TestCorpusSummary(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)

    def test_summarizes_counts_and_jitter_across_clips(self):
        clip1 = make_meta(
            "clip_0001", "valid_putt", [0.0, 0.010, 0.020], with_ground_truth=True, tier2=True
        )
        clip2 = make_meta("clip_0002", "adversarial", [0.0, 0.008])
        for meta in (clip1, clip2):
            save_clip(self.root, meta)
            append_index_entry(self.root, meta)

        summary = corpus_summary(self.root)

        self.assertEqual(summary["clip_count"], 2)
        self.assertEqual(summary["by_kind"], {"valid_putt": 1, "adversarial": 1})
        self.assertEqual(summary["with_ground_truth"], 1)
        self.assertEqual(summary["with_tier2_labels"], 1)
        # 3 intervals total across both clips: 10, 10, 8 ms.
        self.assertAlmostEqual(summary["jitter"]["median"], 10.0)

    def test_empty_corpus(self):
        summary = corpus_summary(self.root)

        self.assertEqual(summary["clip_count"], 0)
        self.assertEqual(summary["by_kind"], {})
        self.assertEqual(summary["jitter"]["median"], 0.0)


if __name__ == "__main__":
    unittest.main()
