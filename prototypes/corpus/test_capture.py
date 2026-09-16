import shutil
import tempfile
import time
import unittest
from pathlib import Path

import numpy as np

from capture import (
    AutoTrigger,
    CaptureWorker,
    RingBuffer,
    build_clip_metadata,
    fourcc_to_str,
    save_ring_buffer_as_clip,
)
from corpus import load_clip, read_index


def gray_frame(fill=128, shape=(4, 4)):
    return np.full(shape, fill, dtype=np.uint8)


class TestRingBuffer(unittest.TestCase):
    def test_drops_oldest_once_full(self):
        ring = RingBuffer(maxlen=3)

        for i in range(5):
            ring.append(float(i), gray_frame(i))

        frames = ring.frames()
        self.assertEqual(len(ring), 3)
        self.assertEqual([t for t, _ in frames], [2.0, 3.0, 4.0])

    def test_frames_are_returned_oldest_first(self):
        ring = RingBuffer(maxlen=10)
        ring.append(1.0, gray_frame())
        ring.append(2.0, gray_frame())

        timestamps = [t for t, _ in ring.frames()]

        self.assertEqual(timestamps, [1.0, 2.0])

    def test_latest_returns_most_recently_appended(self):
        ring = RingBuffer(maxlen=10)
        ring.append(1.0, gray_frame(fill=1))
        ring.append(2.0, gray_frame(fill=2))

        t, frame = ring.latest()

        self.assertEqual(t, 2.0)
        self.assertEqual(frame[0, 0], 2)

    def test_latest_on_empty_ring_returns_none(self):
        ring = RingBuffer(maxlen=10)

        self.assertIsNone(ring.latest())


class TestFourccToStr(unittest.TestCase):
    def test_decodes_mjpg(self):
        # cv2.VideoWriter_fourcc(*'MJPG') packs bytes little-endian into a float.
        packed = float(ord("M") | ord("J") << 8 | ord("P") << 16 | ord("G") << 24)

        self.assertEqual(fourcc_to_str(packed), "MJPG")


class TestBuildClipMetadata(unittest.TestCase):
    def test_timestamps_are_relative_to_first_frame(self):
        frames = [(10.0, gray_frame()), (10.010, gray_frame()), (10.021, gray_frame())]

        meta = build_clip_metadata(
            "clip_0001", "valid_putt", frames,
            requested={"fps": 120}, actual={"fps": 119.7},
            device_name="Test Cam", diagnostic_props={},
        )

        self.assertAlmostEqual(meta.frames.timestamps_s[0], 0.0)
        self.assertAlmostEqual(meta.frames.timestamps_s[1], 0.010, places=3)
        self.assertAlmostEqual(meta.frames.timestamps_s[2], 0.021, places=3)
        self.assertEqual(meta.frames.count, 3)

    def test_interval_ms_matches_harness_spec_clip_json_field_names(self):
        # docs/GolfSimVision-Phase0-Harness-Specs.md's clip.json example uses
        # {"median":..., "p95":..., "max":..., "jitter_sd":...} -- no gap_count nested
        # inside interval_ms (that lives only at Timing.gap_count).
        frames = [(0.0, gray_frame()), (0.010, gray_frame()), (0.020, gray_frame())]

        meta = build_clip_metadata(
            "clip_0001", "valid_putt", frames,
            requested={}, actual={}, device_name="", diagnostic_props={},
        )

        self.assertEqual(
            set(meta.timing.interval_ms.keys()), {"median", "p95", "max", "jitter_sd"}
        )

    def test_measured_fps_derived_from_median_interval(self):
        # 10ms median interval -> 100fps measured, regardless of requested/actual.
        frames = [(0.0, gray_frame()), (0.010, gray_frame()), (0.020, gray_frame())]

        meta = build_clip_metadata(
            "clip_0001", "valid_putt", frames,
            requested={"fps": 120}, actual={"fps": 120},
            device_name="", diagnostic_props={},
        )

        self.assertAlmostEqual(meta.timing.measured_fps, 100.0, places=0)

    def test_empty_frames_does_not_crash(self):
        meta = build_clip_metadata(
            "clip_0001", "adversarial", [],
            requested={}, actual={}, device_name="", diagnostic_props={},
        )

        self.assertEqual(meta.frames.count, 0)
        self.assertEqual(meta.timing.measured_fps, 0.0)


class TestSaveRingBufferAsClip(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)

    def test_writes_frames_clip_json_and_index_entry(self):
        ring = RingBuffer(maxlen=10)
        ring.append(0.0, gray_frame())
        ring.append(0.010, gray_frame())

        meta = save_ring_buffer_as_clip(
            self.root, ring, "valid_putt",
            requested={"fps": 120}, actual={"fps": 120},
            device_name="Test Cam", diagnostic_props={"CAP_PROP_GAIN": 4.0},
        )

        self.assertTrue((self.root / meta.clip_id / "frames" / "000000.png").exists())
        self.assertTrue((self.root / meta.clip_id / "frames" / "000001.png").exists())
        self.assertEqual(load_clip(self.root, meta.clip_id).clip_id, meta.clip_id)
        self.assertEqual(len(read_index(self.root)), 1)

    def test_assigns_sequential_clip_ids_across_saves(self):
        ring = RingBuffer(maxlen=10)
        ring.append(0.0, gray_frame())

        meta1 = save_ring_buffer_as_clip(
            self.root, ring, "valid_putt",
            requested={}, actual={}, device_name="", diagnostic_props={},
        )
        meta2 = save_ring_buffer_as_clip(
            self.root, ring, "adversarial",
            requested={}, actual={}, device_name="", diagnostic_props={},
        )

        self.assertEqual(meta1.clip_id, "clip_0001")
        self.assertEqual(meta2.clip_id, "clip_0002")


class FakeCap:
    """A cv2.VideoCapture stand-in that reads frames as fast as it's asked to."""

    def __init__(self, fail_after: int | None = None):
        self._n = 0
        self.fail_after = fail_after

    def read(self):
        self._n += 1
        if self.fail_after is not None and self._n > self.fail_after:
            return False, None
        return True, gray_frame(fill=self._n % 256)


class TestCaptureWorker(unittest.TestCase):
    def test_pushes_frames_into_ring_buffer_until_stopped(self):
        ring = RingBuffer(maxlen=1000)
        worker = CaptureWorker(FakeCap(), ring)

        worker.start()
        time.sleep(0.05)
        worker.stop()

        self.assertGreater(len(ring), 0)
        self.assertIsNone(worker.error)

    def test_records_error_and_stops_on_failed_read(self):
        ring = RingBuffer(maxlen=1000)
        worker = CaptureWorker(FakeCap(fail_after=3), ring)

        worker.start()
        worker.stop()

        self.assertEqual(worker.error, "frame read failed")
        self.assertEqual(len(ring), 3)


class TestAutoTrigger(unittest.TestCase):
    def test_first_frame_never_fires(self):
        trigger = AutoTrigger()

        self.assertFalse(trigger.update(gray_frame(fill=100, shape=(20, 20))))

    def test_identical_frames_do_not_fire(self):
        trigger = AutoTrigger()
        frame = gray_frame(fill=100, shape=(20, 20))
        trigger.update(frame)

        self.assertFalse(trigger.update(frame.copy()))

    def test_large_change_fires(self):
        trigger = AutoTrigger(threshold=25.0, min_changed_fraction=0.02)
        trigger.update(gray_frame(fill=0, shape=(20, 20)))

        self.assertTrue(trigger.update(gray_frame(fill=255, shape=(20, 20))))


if __name__ == "__main__":
    unittest.main()
