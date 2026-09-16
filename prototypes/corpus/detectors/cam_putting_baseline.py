"""Baseline detector -- a port of cam-putting-py's approach, per
docs/GolfSimVision-Phase0-Harness-Specs.md "P0-1" section 5: "a straight port of
cam-putting-py's approach (HSV + two-sample gate + ball-radius scale)."

Ported from ../../../example-apps/cam-putting-py/ball_tracking.py (read-only reference),
its core tracking loop around lines 890-1100: find the largest matching contour each
frame, lock a ball-radius-to-mm scale once a stable start circle is found, time the ball
crossing two x-gates, and compute speed/HLA from the two-sample displacement. Left out:
everything that's GUI/config machinery in the original (trackbars, config.ini, replay
video writers, PS4-camera decode) -- none of that is "the approach."

One deliberate adaptation: capture.py stores 8-bit grayscale PNGs (harness spec section 1
is explicit about this), so there is no hue/saturation channel to threshold on. The
original's "white ball" HSV range (bright, low-saturation) is really a brightness
threshold in disguise, so this port uses cv2.inRange on the grayscale value directly --
functionally the same selectivity for a white ball, the only case a grayscale corpus can
support. This is a documented substitution, not a silent one.
"""

from __future__ import annotations

import math
from dataclasses import dataclass

import cv2
import numpy as np

GOLF_BALL_RADIUS_MM = 21.33

# Grayscale equivalent of cam-putting-py's "white2" HSV range (bright, low-saturation).
DEFAULT_BRIGHTNESS_MIN = 200
DEFAULT_BRIGHTNESS_MAX = 255


def launch_angle_deg(p1: tuple[float, float], p2: tuple[float, float]) -> float:
    """Port of ball_tracking.py's GetAngle(), without the flipImage branch (n/a here)."""
    x1, y1 = p1
    x2, y2 = p2
    rads = math.atan2(-(y2 - y1), x2 - x1)
    return math.degrees(rads) * -1  # matches the caller's `GetAngle(...) * -1` usage


@dataclass
class PuttMeasurement:
    speed_mph: float
    hla_deg: float
    track_points: int
    t_start: float
    t_end: float


class CamPuttingBaselineDetector:
    """Detector Protocol implementation: reset() / push_frame(t, frame) -> Optional[PuttMeasurement].

    gate1_frac/gate2_frac place the two gates as fractions of frame width, since this
    corpus has no config.ini-defined detection zone the way cam-putting-py does.
    """

    def __init__(
        self,
        brightness_min: int = DEFAULT_BRIGHTNESS_MIN,
        brightness_max: int = DEFAULT_BRIGHTNESS_MAX,
        gate1_frac: float = 0.3,
        gate2_frac: float = 0.7,
        min_radius_px: float = 5.0,
        entry_dx_px: float = 50.0,
        hla_limit_deg: float = 40.0,
        speed_min_mph: float = 0.5,
        speed_max_mph: float = 25.0,
    ):
        self.brightness_min = brightness_min
        self.brightness_max = brightness_max
        self.gate1_frac = gate1_frac
        self.gate2_frac = gate2_frac
        self.min_radius_px = min_radius_px
        self.entry_dx_px = entry_dx_px
        self.hla_limit_deg = hla_limit_deg
        self.speed_min_mph = speed_min_mph
        self.speed_max_mph = speed_max_mph
        self.reset()

    def reset(self) -> None:
        self._gate1_x: float | None = None
        self._gate2_x: float | None = None
        self._started = False
        self._entered = False
        self._start_circle: tuple[float, float, float] | None = None
        self._pixel_mm_ratio: float | None = None
        self._tim1: float | None = None
        self._track_points: list[tuple[float, float]] = []
        self.rejections: list[str] = []

    def _largest_ball_contour(self, mask: np.ndarray) -> tuple[float, float, float] | None:
        contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        if not contours:
            return None
        largest = max(contours, key=cv2.contourArea)
        (x, y), radius = cv2.minEnclosingCircle(largest)
        if radius < self.min_radius_px:
            return None
        return (x, y, radius)

    def push_frame(self, t: float, frame: np.ndarray) -> PuttMeasurement | None:
        if self._gate1_x is None:
            width = frame.shape[1]
            self._gate1_x = width * self.gate1_frac
            self._gate2_x = width * self.gate2_frac

        mask = cv2.inRange(frame, self.brightness_min, self.brightness_max)
        circle = self._largest_ball_contour(mask)
        if circle is None:
            return None
        x, y, radius = circle

        if not self._started:
            self._start_circle = circle
            self._pixel_mm_ratio = radius / GOLF_BALL_RADIUS_MM
            self._started = True
            self._entered = False
            self._track_points = []
            return None

        if not self._entered:
            if x >= self._gate1_x:
                self._tim1 = t
                self._entered = True
                self._track_points = [(x, y)]
            return None

        self._track_points.append((x, y))

        if x <= self._gate2_x or (x - self._track_points[0][0]) < self.entry_dx_px:
            return None

        return self._try_fire(t, (x, y))

    def _try_fire(self, t_end: float, end_pos: tuple[float, float]) -> PuttMeasurement | None:
        start_pos = (self._start_circle[0], self._start_circle[1])
        entry_pos = self._track_points[0]
        dx = end_pos[0] - entry_pos[0]
        dy = end_pos[1] - entry_pos[1]
        distance_px = math.hypot(dx, dy)
        distance_mm = distance_px / self._pixel_mm_ratio
        elapsed_s = t_end - self._tim1

        result = None
        if elapsed_s <= 0:
            self.rejections.append("non_positive_elapsed_time")
        else:
            speed_mph = (distance_mm / 1_000_000) / elapsed_s * 3600 * 0.621371
            hla_deg = launch_angle_deg(start_pos, end_pos)
            if not (self.speed_min_mph <= speed_mph <= self.speed_max_mph):
                self.rejections.append("speed_out_of_range")
            elif abs(hla_deg) >= self.hla_limit_deg:
                self.rejections.append("hla_out_of_range")
            else:
                result = PuttMeasurement(
                    speed_mph=speed_mph,
                    hla_deg=hla_deg,
                    track_points=len(self._track_points),
                    t_start=self._tim1,
                    t_end=t_end,
                )

        # Re-arm: a new "start" must be re-established before the next shot can fire,
        # matching the original's per-shot startCircle/pixelmmratio reset.
        self._started = False
        self._entered = False
        return result


def create_detector() -> CamPuttingBaselineDetector:
    return CamPuttingBaselineDetector()
