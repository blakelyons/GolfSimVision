"""Speed/HLA calculation from a tracked ball path -- ported from V1
(SimLinkPutting)'s backend/detection/putt_calculator.py, unmodified.

Pure function of the track's own (x, y, pixels_per_mm, timestamp) tuples --
no wall-clock reads, no state -- so it needs no adaptation for any calling
context.
"""

import math
import numpy as np
from dataclasses import dataclass
from typing import Literal


GOLF_BALL_DIAMETER_MM = 42.67
MM_PER_INCH = 25.4
INCHES_PER_YARD = 36


MM_PER_FOOT = 304.8

# Only use the first MOTION_WINDOW_S seconds of track data for velocity fitting.
# After this window the ball may have stopped rolling; including stopped-ball points
# in the linear fit drags the velocity estimate toward zero.
MOTION_WINDOW_S = 0.75

# Physical-plausibility bounds for a real putt. A result outside these bounds
# is not a putt -- it's noise from a thin/short track being read as a real
# roll. Reject it here rather than downstream, so there is exactly one
# failure contract (ValueError) for "this track shouldn't become a shot."
MIN_PLAUSIBLE_SPEED_MPH = 0.5
MAX_PLAUSIBLE_SPEED_MPH = 25.0
MAX_PLAUSIBLE_HLA_DEGREES = 40.0


@dataclass
class PuttResult:
    speed_mph: float
    hla_degrees: float
    pixels_per_mm: float
    distance_ft: float = 0.0   # straight-line distance in feet
    speed_fps: float = 0.0     # speed in feet per second
    spin_total: float = 0.0
    spin_axis: float = 0.0


class PuttCalculator:
    def __init__(self, spin_mode: Literal["zero", "bias", "ai"] = "zero",
                 spin_backspin: float = 0.0, spin_sidespin: float = 0.0):
        self.spin_mode = spin_mode
        self.spin_backspin = spin_backspin
        self.spin_sidespin = spin_sidespin

    def calculate(self, track: list[tuple], frame_width: int, frame_height: int,
                  manual_ppm: float = 0.0,
                  hla_calibration_offset_degrees: float = 0.0) -> PuttResult:
        """
        track: list of (norm_x, norm_y, pixels_per_mm, timestamp)
        Returns speed in MPH and HLA in degrees (calibration offset already applied).

        Raises ValueError if the track's timing/scale is invalid, or if the
        resulting speed/HLA is outside physically-plausible bounds for a putt --
        callers should treat both as "no shot," not attempt to recover a result.
        """
        if len(track) < 2:
            raise ValueError("Need at least 2 track points")

        avg_ppm = (sum(p[2] for p in track) / len(track)) if manual_ppm == 0 else manual_ppm

        dt = track[-1][3] - track[0][3]

        if dt <= 0 or avg_ppm <= 0:
            raise ValueError("Invalid track timing or scale")

        xs_mm = [p[0] * frame_width / avg_ppm for p in track]
        ys_mm = [p[1] * frame_height / avg_ppm for p in track]
        ts = [p[3] for p in track]

        t_cutoff = ts[0] + MOTION_WINDOW_S
        motion_idx = next((i for i, t in enumerate(ts) if t > t_cutoff), len(ts))
        motion_idx = max(motion_idx, 2)
        xs_fit = xs_mm[:motion_idx]
        ys_fit = ys_mm[:motion_idx]
        ts_fit = ts[:motion_idx]

        if len(xs_fit) >= 3:
            ts_rel = np.array(ts_fit) - ts_fit[0]

            vx = float(np.polyfit(ts_rel, xs_fit, 1)[0])
            vy = float(np.polyfit(ts_rel, ys_fit, 1)[0])
            speed_ms = math.sqrt(vx**2 + vy**2) / 1000.0

            x_range = float(xs_fit[-1] - xs_fit[0])
            if abs(x_range) >= 10.0:
                slope = float(np.polyfit(xs_fit, ys_fit, 1)[0])
                hla = math.degrees(math.atan2(slope, 1.0))
            else:
                hla = math.degrees(math.atan2(ys_fit[-1] - ys_fit[0], xs_fit[-1] - xs_fit[0]))
        else:
            dx_mm = xs_fit[-1] - xs_fit[0]
            dy_mm = ys_fit[-1] - ys_fit[0]
            dist_mm = math.sqrt(dx_mm**2 + dy_mm**2)
            dt_fit = ts_fit[-1] - ts_fit[0]
            speed_ms = (dist_mm / 1000.0) / max(dt_fit, 1e-6)
            hla = math.degrees(math.atan2(dy_mm, dx_mm))

        speed_mph = speed_ms * 2.23694

        hla -= hla_calibration_offset_degrees

        dx_mm = xs_mm[-1] - xs_mm[0]
        dy_mm = ys_mm[-1] - ys_mm[0]
        dist_mm = math.sqrt(dx_mm**2 + dy_mm**2)

        spin_total, spin_axis = self._compute_spin()

        distance_ft = round(dist_mm / MM_PER_FOOT, 2)
        speed_fps = round(speed_ms * 3.28084, 2)
        speed_mph = round(speed_mph, 2)
        hla = round(hla, 2)

        if not (MIN_PLAUSIBLE_SPEED_MPH <= speed_mph <= MAX_PLAUSIBLE_SPEED_MPH):
            raise ValueError(
                f"Implausible speed {speed_mph} mph -- outside "
                f"[{MIN_PLAUSIBLE_SPEED_MPH}, {MAX_PLAUSIBLE_SPEED_MPH}] mph"
            )
        if abs(hla) > MAX_PLAUSIBLE_HLA_DEGREES:
            raise ValueError(
                f"Implausible HLA {hla} deg -- outside +/-{MAX_PLAUSIBLE_HLA_DEGREES} deg"
            )

        return PuttResult(
            speed_mph=speed_mph,
            hla_degrees=hla,
            pixels_per_mm=round(avg_ppm, 4),
            distance_ft=distance_ft,
            speed_fps=speed_fps,
            spin_total=spin_total,
            spin_axis=spin_axis,
        )

    def _compute_spin(self) -> tuple[float, float]:
        if self.spin_mode == "zero":
            return 0.0, 0.0
        elif self.spin_mode == "bias":
            return self.spin_backspin, self.spin_sidespin
        else:
            return 0.0, 0.0
