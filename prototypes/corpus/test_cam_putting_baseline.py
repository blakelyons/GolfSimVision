import unittest

import cv2
import numpy as np

from detectors.cam_putting_baseline import CamPuttingBaselineDetector, launch_angle_deg


def frame_with_ball(x: int, y: int, radius: int = 8, size=(120, 200)) -> np.ndarray:
    frame = np.zeros(size, dtype=np.uint8)
    cv2.circle(frame, (x, y), radius, 255, -1)
    return frame


def feed_moving_ball(detector, y=60, x_start=10, x_end=190, step=4, radius=8, size=(120, 200), fps=120.0):
    """Push a series of frames where a bright ball moves left-to-right at constant y."""
    results = []
    t = 0.0
    x = x_start
    while x <= x_end:
        result = detector.push_frame(t, frame_with_ball(x, y, radius, size))
        if result is not None:
            results.append(result)
        t += 1.0 / fps
        x += step
    return results


class TestLaunchAngleDeg(unittest.TestCase):
    def test_straight_line_is_zero_degrees(self):
        self.assertAlmostEqual(launch_angle_deg((0, 0), (10, 0)), 0.0)

    def test_ball_moving_up_is_negative(self):
        # In image coordinates, up is a smaller y; the ported formula's double negation
        # (atan2(-dy, dx), then the caller's *-1) makes "up" come out negative.
        angle = launch_angle_deg((0, 10), (10, 0))
        self.assertLess(angle, 0)

    def test_ball_moving_down_is_positive(self):
        angle = launch_angle_deg((0, 0), (10, 10))
        self.assertGreater(angle, 0)


class TestCamPuttingBaselineDetector(unittest.TestCase):
    def test_fires_once_for_a_ball_crossing_both_gates_in_a_straight_line(self):
        detector = CamPuttingBaselineDetector()

        results = feed_moving_ball(detector)

        self.assertEqual(len(results), 1)
        self.assertGreater(results[0].speed_mph, 0)
        self.assertAlmostEqual(results[0].hla_deg, 0.0, delta=1.0)

    def test_no_ball_in_frame_never_fires(self):
        detector = CamPuttingBaselineDetector()
        blank = np.zeros((120, 200), dtype=np.uint8)

        for i in range(20):
            result = detector.push_frame(i / 120.0, blank)
            self.assertIsNone(result)

    def test_reset_clears_state_between_clips(self):
        detector = CamPuttingBaselineDetector()
        feed_moving_ball(detector)  # establishes started/entered state

        detector.reset()

        self.assertFalse(detector._started)
        self.assertFalse(detector._entered)
        self.assertEqual(detector.rejections, [])

    def test_speed_scales_with_travel_time(self):
        slow = CamPuttingBaselineDetector()
        fast = CamPuttingBaselineDetector()

        slow_results = feed_moving_ball(slow, step=2)   # smaller steps -> more elapsed time
        fast_results = feed_moving_ball(fast, step=6)   # larger steps -> less elapsed time

        self.assertEqual(len(slow_results), 1)
        self.assertEqual(len(fast_results), 1)
        self.assertLess(slow_results[0].speed_mph, fast_results[0].speed_mph)

    def test_rejects_out_of_range_speed(self):
        # An absurdly large single-frame jump between gates yields an implausible speed.
        detector = CamPuttingBaselineDetector(speed_max_mph=0.001)

        results = feed_moving_ball(detector)

        self.assertEqual(results, [])
        self.assertIn("speed_out_of_range", detector.rejections)


if __name__ == "__main__":
    unittest.main()
