import shutil
import tempfile
import unittest
from pathlib import Path

import cv2
import numpy as np

from corpus import (
    CameraProps,
    ClipMetadata,
    Frames,
    GroundTruth,
    Timing,
    append_index_entry,
    frames_dir,
    save_clip,
)
from detectors.cam_putting_baseline import PuttMeasurement
from replay import iterate_clip_frames, run_detector_on_clip, score_corpus


def write_synthetic_clip(root: Path, clip_id: str, kind: str, n_frames: int, ground_truth=None):
    timestamps = [i / 120.0 for i in range(n_frames)]
    meta = ClipMetadata(
        clip_id=clip_id,
        created_utc="2026-09-16T00:00:00Z",
        kind=kind,
        camera=CameraProps(requested={}, actual={}),
        frames=Frames(count=n_frames, first_index=0, timestamps_s=timestamps),
        timing=Timing(measured_fps=120.0, interval_ms={}, gap_count=0),
        ground_truth=ground_truth or GroundTruth(),
    )
    save_clip(root, meta)
    append_index_entry(root, meta)

    out_dir = frames_dir(root, clip_id)
    out_dir.mkdir(parents=True, exist_ok=True)
    blank = np.zeros((20, 20), dtype=np.uint8)
    for i in range(n_frames):
        cv2.imwrite(str(out_dir / f"{i:06d}.png"), blank)
    return meta


class NeverFiresDetector:
    def reset(self):
        self.rejections = []

    def push_frame(self, t, frame):
        return None


class FiresOnLastFrameDetector:
    def __init__(self, speed_mph=5.0, hla_deg=0.0):
        self.speed_mph = speed_mph
        self.hla_deg = hla_deg
        self.reset()

    def reset(self):
        self._count = 0
        self._total = None
        self.rejections = []

    def push_frame(self, t, frame):
        self._count += 1
        # Fires on the 3rd call of each clip (test clips are 3 frames).
        if self._count == 3:
            return PuttMeasurement(self.speed_mph, self.hla_deg, track_points=2, t_start=0.0, t_end=t)
        return None


class FiresTwiceDetector:
    def reset(self):
        self._count = 0
        self.rejections = []

    def push_frame(self, t, frame):
        self._count += 1
        if self._count in (1, 2):
            return PuttMeasurement(5.0, 0.0, track_points=2, t_start=0.0, t_end=t)
        return None


class RejectingDetector:
    def reset(self):
        self.rejections = []

    def push_frame(self, t, frame):
        self.rejections.append("speed_out_of_range")
        return None


class TestIterateClipFrames(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)

    def test_yields_timestamps_paired_with_frames_in_order(self):
        write_synthetic_clip(self.root, "clip_0001", "valid_putt", n_frames=3)

        pairs = list(iterate_clip_frames(self.root, "clip_0001"))

        self.assertEqual(len(pairs), 3)
        self.assertEqual([t for t, _ in pairs], [0.0, 1 / 120.0, 2 / 120.0])
        self.assertEqual(pairs[0][1].shape, (20, 20))


class TestRunDetectorOnClip(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)

    def test_resets_detector_before_feeding_frames(self):
        write_synthetic_clip(self.root, "clip_0001", "valid_putt", n_frames=3)
        detector = FiresOnLastFrameDetector()

        measurements = run_detector_on_clip(detector, self.root, "clip_0001")

        self.assertEqual(len(measurements), 1)
        self.assertAlmostEqual(measurements[0].speed_mph, 5.0)


class TestScoreCorpus(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)

    def test_true_positive_and_false_negative(self):
        write_synthetic_clip(self.root, "clip_0001", "valid_putt", n_frames=3)
        write_synthetic_clip(self.root, "clip_0002", "valid_putt", n_frames=3)

        # clip_0001 fires (TP-shaped), clip_0002 never fires (FN) -- use two detector
        # instances scored separately since score_corpus takes one detector for the corpus.
        scorecard = score_corpus(NeverFiresDetector(), self.root)
        self.assertEqual(scorecard["false_negatives"], 2)
        self.assertEqual(scorecard["true_positives"], 0)

        scorecard = score_corpus(FiresOnLastFrameDetector(), self.root)
        self.assertEqual(scorecard["true_positives"], 2)
        self.assertEqual(scorecard["false_negatives"], 0)
        # Both TP measurements report track_points=2 -- a real distribution, not just
        # a min/max/mean summary.
        self.assertEqual(scorecard["track_points"]["counts"], {"2": 2})
        self.assertEqual(scorecard["track_points"]["mean"], 2.0)

    def test_false_positive_on_adversarial_clip_that_fires(self):
        write_synthetic_clip(self.root, "clip_0001", "adversarial", n_frames=3)

        scorecard = score_corpus(FiresOnLastFrameDetector(), self.root)

        self.assertEqual(scorecard["false_positives"], 1)
        self.assertEqual(scorecard["true_positives"], 0)

    def test_double_fire_counted_separately_from_true_positive(self):
        write_synthetic_clip(self.root, "clip_0001", "valid_putt", n_frames=3)

        scorecard = score_corpus(FiresTwiceDetector(), self.root)

        self.assertEqual(scorecard["double_fires"], 1)
        self.assertEqual(scorecard["true_positives"], 0)  # exactly-one is required for TP

    def test_speed_and_hla_error_computed_against_ground_truth(self):
        write_synthetic_clip(
            self.root, "clip_0001", "valid_putt", n_frames=3,
            ground_truth=GroundTruth(method="two_gate_manual", speed_mph=5.5, hla_deg=2.0),
        )

        scorecard = score_corpus(FiresOnLastFrameDetector(speed_mph=5.0, hla_deg=0.0), self.root)

        # |5.0 - 5.5| / 5.5 * 100 ~= 9.09%
        self.assertAlmostEqual(scorecard["speed_error_pct"]["mean"], 9.0909, places=3)
        self.assertAlmostEqual(scorecard["hla_error_deg"]["mean"], 2.0, places=3)

    def test_rejections_aggregated_by_reason_code(self):
        write_synthetic_clip(self.root, "clip_0001", "valid_putt", n_frames=3)

        scorecard = score_corpus(RejectingDetector(), self.root)

        self.assertEqual(scorecard["rejections"], {"speed_out_of_range": 3})

    def test_empty_corpus(self):
        scorecard = score_corpus(NeverFiresDetector(), self.root)

        self.assertEqual(scorecard["clip_count"], 0)
        self.assertEqual(scorecard["true_positives"], 0)


if __name__ == "__main__":
    unittest.main()
