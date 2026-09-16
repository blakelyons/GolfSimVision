import json
import shutil
import tempfile
import unittest
from pathlib import Path

from corpus import (
    CameraProps,
    ClipMetadata,
    Frames,
    Timing,
    load_clip,
    save_clip,
)
from label import (
    mark_tier2_labelled,
    ramp_release_speed_mph,
    record_ramp_ground_truth,
    record_two_gate_ground_truth,
    save_tier2_labels,
    two_gate_speed_mph,
)


def make_meta(clip_id, timestamps):
    return ClipMetadata(
        clip_id=clip_id,
        created_utc="2026-09-16T00:00:00Z",
        kind="valid_putt",
        camera=CameraProps(requested={}, actual={}),
        frames=Frames(count=len(timestamps), first_index=0, timestamps_s=timestamps),
        timing=Timing(measured_fps=120.0, interval_ms={}, gap_count=0),
    )


class TestTwoGateSpeed(unittest.TestCase):
    def test_computes_mph_from_elapsed_time_between_gates(self):
        # 400mm in 0.05s = 8000 mm/s = 17.8955 mph
        timestamps = [0.0, 0.05, 0.10, 0.15]

        speed = two_gate_speed_mph(timestamps, gate1_frame=1, gate2_frame=2, gate_distance_mm=400.0)

        self.assertAlmostEqual(speed, 17.8955, places=3)

    def test_gate2_before_gate1_raises(self):
        timestamps = [0.0, 0.05, 0.10]

        with self.assertRaises(ValueError):
            two_gate_speed_mph(timestamps, gate1_frame=2, gate2_frame=0, gate_distance_mm=400.0)


class TestRampReleaseSpeed(unittest.TestCase):
    def test_matches_harness_spec_reference_table(self):
        # docs/GolfSimVision-Phase0-Harness-Specs.md P0-1 section 3 table.
        self.assertAlmostEqual(ramp_release_speed_mph(10), 2.6, delta=0.05)
        self.assertAlmostEqual(ramp_release_speed_mph(20), 3.7, delta=0.05)
        self.assertAlmostEqual(ramp_release_speed_mph(30), 4.6, delta=0.05)
        self.assertAlmostEqual(ramp_release_speed_mph(45), 5.6, delta=0.05)
        self.assertAlmostEqual(ramp_release_speed_mph(60), 6.5, delta=0.05)


class TestRecordGroundTruth(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        save_clip(self.root, make_meta("clip_0001", [0.0, 0.05, 0.10, 0.15]))

    def test_two_gate_writes_ground_truth_into_clip_json(self):
        gt = record_two_gate_ground_truth(
            self.root, "clip_0001", gate1_frame=1, gate2_frame=2, gate_distance_mm=400.0, hla_deg=-1.2
        )

        self.assertEqual(gt.method, "two_gate_manual")
        self.assertAlmostEqual(gt.speed_mph, 17.8955, places=3)
        self.assertEqual(gt.gate_frames, [1, 2])

        reloaded = load_clip(self.root, "clip_0001")
        self.assertEqual(reloaded.ground_truth.method, "two_gate_manual")

    def test_ramp_writes_ground_truth_into_clip_json(self):
        gt = record_ramp_ground_truth(self.root, "clip_0001", release_height_cm=30)

        self.assertEqual(gt.method, "ramp_release_height")
        self.assertAlmostEqual(gt.speed_mph, 4.6, delta=0.05)


class TestTier2Labels(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        save_clip(self.root, make_meta("clip_0001", [0.0, 0.05]))

    def test_mark_tier2_labelled_flips_flag(self):
        mark_tier2_labelled(self.root, "clip_0001")

        self.assertTrue(load_clip(self.root, "clip_0001").labels_tier2)

    def test_save_tier2_labels_writes_file_and_flips_flag(self):
        labels = {0: (100, 50), 1: (110, 52)}

        path = save_tier2_labels(self.root, "clip_0001", labels)

        self.assertTrue(path.exists())
        data = json.loads(path.read_text(encoding="utf-8"))
        self.assertEqual(data["0"], [100, 50])
        self.assertTrue(load_clip(self.root, "clip_0001").labels_tier2)


if __name__ == "__main__":
    unittest.main()
