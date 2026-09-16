import shutil
import tempfile
import unittest
from pathlib import Path

from corpus import (
    CameraProps,
    ClipMetadata,
    Conditions,
    Frames,
    Geometry,
    GroundTruth,
    Timing,
    append_index_entry,
    load_clip,
    next_clip_id,
    read_index,
    save_clip,
)


def make_meta(clip_id="clip_0001", kind="valid_putt", with_ground_truth=False):
    gt = (
        GroundTruth(method="two_gate_manual", speed_mph=4.83, hla_deg=-1.2)
        if with_ground_truth
        else GroundTruth()
    )
    return ClipMetadata(
        clip_id=clip_id,
        created_utc="2026-09-16T00:00:00Z",
        kind=kind,
        camera=CameraProps(
            requested={"width": 1280, "height": 720, "fps": 120, "fourcc": "MJPG"},
            actual={"width": 1280, "height": 720, "fps": 120, "fourcc": "MJPG"},
            device_name="Test Cam",
        ),
        geometry=Geometry(camera_height_in=31.5, mount="overhead"),
        conditions=Conditions(lighting="ambient", surface="mat", ball="white"),
        frames=Frames(count=3, first_index=0, timestamps_s=[0.0, 0.00834, 0.01661]),
        timing=Timing(measured_fps=119.7, interval_ms={"median": 8.35}, gap_count=0),
        ground_truth=gt,
    )


class TestClipRoundTrip(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)

    def test_save_then_load_round_trips_all_fields(self):
        meta = make_meta(with_ground_truth=True)
        save_clip(self.root, meta)

        loaded = load_clip(self.root, meta.clip_id)

        self.assertEqual(loaded, meta)

    def test_save_creates_clip_directory(self):
        meta = make_meta()
        save_clip(self.root, meta)

        self.assertTrue((self.root / meta.clip_id).is_dir())
        self.assertTrue((self.root / meta.clip_id / "clip.json").is_file())

    def test_load_fills_defaults_for_missing_optional_sections(self):
        # A hand-edited or older clip.json might omit optional sections entirely.
        clip_dir = self.root / "clip_0002"
        clip_dir.mkdir(parents=True)
        (clip_dir / "clip.json").write_text(
            """
            {
              "clip_id": "clip_0002",
              "created_utc": "2026-09-16T00:00:00Z",
              "kind": "adversarial",
              "camera": {"requested": {}, "actual": {}},
              "frames": {"count": 1, "first_index": 0, "timestamps_s": [0.0]},
              "timing": {"measured_fps": 120.0, "interval_ms": {}, "gap_count": 0}
            }
            """,
            encoding="utf-8",
        )

        loaded = load_clip(self.root, "clip_0002")

        self.assertEqual(loaded.geometry, Geometry())
        self.assertEqual(loaded.ground_truth, GroundTruth())
        self.assertFalse(loaded.labels_tier2)


class TestIndex(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)

    def test_read_index_on_missing_file_returns_empty_list(self):
        self.assertEqual(read_index(self.root), [])

    def test_append_index_entry_then_read_index(self):
        meta1 = make_meta(clip_id="clip_0001", with_ground_truth=True)
        meta2 = make_meta(clip_id="clip_0002", kind="adversarial")

        append_index_entry(self.root, meta1)
        append_index_entry(self.root, meta2)
        entries = read_index(self.root)

        self.assertEqual(len(entries), 2)
        self.assertEqual(entries[0]["clip_id"], "clip_0001")
        self.assertTrue(entries[0]["has_ground_truth"])
        self.assertEqual(entries[1]["clip_id"], "clip_0002")
        self.assertFalse(entries[1]["has_ground_truth"])


class TestNextClipId(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)

    def test_first_id_on_empty_corpus(self):
        self.assertEqual(next_clip_id(self.root), "clip_0001")

    def test_next_id_skips_existing(self):
        (self.root / "clip_0001").mkdir(parents=True)
        (self.root / "clip_0002").mkdir(parents=True)

        self.assertEqual(next_clip_id(self.root), "clip_0003")

    def test_next_id_fills_a_gap(self):
        (self.root / "clip_0001").mkdir(parents=True)
        (self.root / "clip_0003").mkdir(parents=True)

        self.assertEqual(next_clip_id(self.root), "clip_0002")


if __name__ == "__main__":
    unittest.main()
