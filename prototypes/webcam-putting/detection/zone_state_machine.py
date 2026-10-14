"""Zone/gateway putt state machine -- ported from V1 (SimLinkPutting)'s
backend/detection/zone_state_machine.py, unmodified.

Unlike the P0-1 corpus's offline replay.py harness (which feeds frames much
faster than real-time and would make this class's internal time.monotonic()
calls meaningless), this is a genuinely live pipeline: real wall-clock time
during TRACKING_TIMEOUT_S / STILL_THRESHOLD_FRAMES / heading-guard timing is
exactly what this class was designed for, so it ports over as-is.
"""

import logging
import math
import time
from enum import Enum
from dataclasses import dataclass, field
from typing import Optional
from .detector import Detection

log = logging.getLogger(__name__)


class PuttState(Enum):
    IDLE = "idle"
    WAITING = "waiting"   # ball present in start zone, not moving
    TRACKING = "tracking" # ball crossed gateway, recording trajectory
    COMPLETE = "complete"


@dataclass
class TrackPoint:
    x: float
    y: float
    pixels_per_mm: float
    ts: float


@dataclass
class ZoneStateMachine:
    # Zones in normalized coords (0-1 relative to frame).
    # Camera is overhead, ball travels LEFT -> RIGHT (increasing X).
    start_zone: dict = field(default_factory=lambda: {"x": 0.05, "y": 0.1, "w": 0.25, "h": 0.8})
    gateway_x: float = 0.55  # vertical threshold; ball must cross rightward to trigger tracking
    # Camera resolution -- only used to convert normalized track displacement to mm
    # for the track-quality gate. Doesn't need to be exact; a rough match to the
    # real camera resolution is enough to distinguish a real roll from jitter.
    frame_width: int = 1280
    frame_height: int = 720

    state: PuttState = PuttState.IDLE
    _track: list[TrackPoint] = field(default_factory=list)
    _still_frames: int = 0
    _last_detection: Optional[Detection] = None
    _last_detection_ts: float = 0.0   # monotonic time of _last_detection
    _tracking_started: float = 0.0   # monotonic time when TRACKING began
    # Accumulates ppm readings from WAITING detections (ball sitting still = most
    # reliable size measurement).  stable_ppm returns the median for use as the
    # authoritative scale in speed calculation and the putter-size guard.
    _ppm_samples: list = field(default_factory=list)
    # After a "ball vanished" trigger, skip track points until the ball has moved
    # meaningfully forward from the seed.  Prevents putter hover/retract detections
    # (which sit near seed_x) from polluting the track and skewing HLA.
    _vanish_filter_active: bool = False
    _vanish_filter_seed_x: float = 0.0
    VANISH_FILTER_MIN_DX = 0.04   # ~50px at 1280px wide -- clears putter face

    # Heading-continuity guard (TRACKING only): once the track has >=2 points,
    # a putt's direction is already established -- a real rolling ball can't
    # instantaneously change heading by a large angle in one ~60fps step, even
    # though its NET path (start -> gateway) can legitimately break/curve over
    # many points. This catches a single corrupted point (an HSV false-positive
    # off the putter/hand/shadow, most likely) before it ever reaches the
    # trajectory fit.
    MAX_HEADING_DEVIATION_DEGREES = 35.0
    # In real mm, not raw pixels -- keeps the check meaningful across zoom levels.
    MIN_STEP_MM_FOR_HEADING_CHECK = 20.0

    STILL_THRESHOLD_FRAMES = 20
    # Track-quality gate (precision over recall, ADR-0007): a track this thin
    # can't be trusted to become a shot.
    MIN_TRACKING_FRAMES = 5
    MIN_TRACK_DISPLACEMENT_MM = 15.0   # ~1/3 of a golf ball diameter (42.67mm)
    MIN_TRACK_DURATION_S = 0.05        # 50ms -- a handful of frames at 60-120fps
    TRACKING_TIMEOUT_S = 3.0
    PPM_SAMPLE_SIZE = 30

    def _warn_if_misconfigured(self):
        sz = self.start_zone
        zone_right = sz["x"] + sz["w"]
        if self.gateway_x < zone_right:
            log.warning(
                "gateway_x (%.3f) is inside the start zone (right edge=%.3f) -- "
                "ball will never cross the gateway while outside the zone.",
                self.gateway_x, zone_right
            )

    def update(self, detection: Optional[Detection]) -> Optional[dict]:
        """Feed a new detection result. Returns a shot dict when COMPLETE."""
        if detection is None:
            if self.state == PuttState.WAITING:
                if self._still_frames >= self.STILL_THRESHOLD_FRAMES:
                    log.info("STATE: WAITING -> TRACKING  ball vanished after %d still frames (assumed putted)",
                             self._still_frames)
                    self.state = PuttState.TRACKING
                    self._track = []
                    # Seed with the last real position so the first mid-flight
                    # detection immediately gives a valid two-point speed calc.
                    if self._last_detection and self._last_detection_ts > 0:
                        self._track.append(TrackPoint(
                            self._last_detection.x, self._last_detection.y,
                            self._last_detection.pixels_per_mm, self._last_detection_ts,
                        ))
                    self._tracking_started = time.monotonic()
                    # Activate forward filter -- ignore detections near seed until
                    # ball has actually moved forward (clears putter hover/retract)
                    self._vanish_filter_active = self._last_detection is not None
                    self._vanish_filter_seed_x = self._last_detection.x if self._last_detection else 0.0
                else:
                    self._still_frames = max(0, self._still_frames - 1)
            elif self.state == PuttState.TRACKING:
                if len(self._track) >= self.MIN_TRACKING_FRAMES:
                    return self._finalize()
                elif self._tracking_timed_out():
                    return self._finalize()
            return None

        if self.state == PuttState.IDLE:
            if self._in_start_zone(detection):
                self._warn_if_misconfigured()
                self.state = PuttState.WAITING
                self._still_frames = 0
                self._last_detection = detection
                self._last_detection_ts = time.monotonic()
                log.info("STATE: IDLE -> WAITING  ball=(%.3f,%.3f) conf=%.2f",
                         detection.x, detection.y, detection.confidence)

        elif self.state == PuttState.WAITING:
            if self._in_start_zone(detection):
                if self._last_detection is not None:
                    dx = abs(detection.x - self._last_detection.x)
                    dy = abs(detection.y - self._last_detection.y)
                    if dx > 0.06 or dy > 0.06:
                        if self._still_frames < self.STILL_THRESHOLD_FRAMES:
                            self._still_frames = 0
                    else:
                        self._still_frames += 1
                        if 0.05 <= detection.pixels_per_mm <= 3.0:
                            self._ppm_samples.append(detection.pixels_per_mm)
                            if len(self._ppm_samples) > self.PPM_SAMPLE_SIZE:
                                self._ppm_samples.pop(0)
                else:
                    self._still_frames += 1
                    if 0.05 <= detection.pixels_per_mm <= 3.0:
                        self._ppm_samples.append(detection.pixels_per_mm)
                        if len(self._ppm_samples) > self.PPM_SAMPLE_SIZE:
                            self._ppm_samples.pop(0)
                self._last_detection = detection
                self._last_detection_ts = time.monotonic()
            else:
                zone_right = self.start_zone["x"] + self.start_zone["w"]
                zone_left = self.start_zone["x"]
                if detection.x > zone_right:
                    log.info("STATE: WAITING -> TRACKING  ball exited start zone (x=%.3f > zone_right=%.3f)",
                             detection.x, zone_right)
                    self.state = PuttState.TRACKING
                    self._tracking_started = time.monotonic()
                    self._track = []
                    self._vanish_filter_active = False
                    if self._last_detection and self._last_detection_ts > 0:
                        self._track.append(TrackPoint(
                            self._last_detection.x, self._last_detection.y,
                            self._last_detection.pixels_per_mm, self._last_detection_ts,
                        ))
                    self._track.append(TrackPoint(
                        detection.x, detection.y, detection.pixels_per_mm, time.monotonic()
                    ))
                elif detection.x < zone_left:
                    log.info("STATE: WAITING -> IDLE  ball moved left of zone (x=%.3f < zone_left=%.3f)",
                             detection.x, zone_left)
                    self.state = PuttState.IDLE
                    self._still_frames = 0
                else:
                    self._still_frames += 1
                    if 0.05 <= detection.pixels_per_mm <= 3.0:
                        self._ppm_samples.append(detection.pixels_per_mm)
                        if len(self._ppm_samples) > self.PPM_SAMPLE_SIZE:
                            self._ppm_samples.pop(0)
                    self._last_detection = detection
                    self._last_detection_ts = time.monotonic()

        elif self.state == PuttState.TRACKING:
            if self._vanish_filter_active:
                if detection.x < self._vanish_filter_seed_x + self.VANISH_FILTER_MIN_DX:
                    if self._tracking_timed_out():
                        return self._finalize()
                    return None
                self._vanish_filter_active = False

            if self._heading_deviation_too_large(detection):
                if self._tracking_timed_out():
                    return self._finalize()
                return None

            self._track.append(TrackPoint(detection.x, detection.y, detection.pixels_per_mm, time.monotonic()))
            if detection.x > self.gateway_x + 0.10:
                return self._finalize()
            if self._tracking_timed_out():
                log.info("STATE: TRACKING -> finalize (timeout with %d points, ball at x=%.3f)",
                         len(self._track), detection.x)
                return self._finalize()

        return None

    def _heading_deviation_too_large(self, detection: Detection) -> bool:
        if len(self._track) < 2:
            return False
        first, last = self._track[0], self._track[-1]
        ppm = last.pixels_per_mm or detection.pixels_per_mm
        if ppm <= 0:
            return False

        est_dx_mm = (last.x - first.x) * self.frame_width / ppm
        est_dy_mm = (last.y - first.y) * self.frame_height / ppm
        if math.hypot(est_dx_mm, est_dy_mm) < self.MIN_STEP_MM_FOR_HEADING_CHECK:
            return False
        est_dx, est_dy = est_dx_mm, est_dy_mm

        step_dx_mm = (detection.x - last.x) * self.frame_width / ppm
        step_dy_mm = (detection.y - last.y) * self.frame_height / ppm
        if math.hypot(step_dx_mm, step_dy_mm) < self.MIN_STEP_MM_FOR_HEADING_CHECK:
            return False
        step_dx, step_dy = step_dx_mm, step_dy_mm

        established_heading = math.degrees(math.atan2(est_dy, est_dx))
        step_heading = math.degrees(math.atan2(step_dy, step_dx))
        deviation = abs((step_heading - established_heading + 180) % 360 - 180)
        if deviation > self.MAX_HEADING_DEVIATION_DEGREES:
            log.debug(
                "TRACK: heading-guard rejected x=%.3f y=%.3f (step heading=%.0f deg, "
                "established=%.0f deg, deviation=%.0f deg)",
                detection.x, detection.y, step_heading, established_heading, deviation,
            )
            return True
        return False

    def _in_start_zone(self, d: Detection) -> bool:
        sz = self.start_zone
        return (sz["x"] <= d.x <= sz["x"] + sz["w"] and
                sz["y"] <= d.y <= sz["y"] + sz["h"])

    def _tracking_timed_out(self) -> bool:
        return time.monotonic() - self._tracking_started > self.TRACKING_TIMEOUT_S

    def _finalize(self) -> Optional[dict]:
        self.state = PuttState.IDLE
        track = self._track
        self._track = []
        self._vanish_filter_active = False

        reason = self._track_quality_issue(track)
        if reason is not None:
            log.info("STATE: TRACKING -> IDLE  finalize rejected -- %s", reason)
            return {"rejected": True, "reason": reason, "point_count": len(track)}

        log.info("STATE: TRACKING -> IDLE  finalize OK -- %d track points  x=[%.3f->%.3f]  y=[%.3f->%.3f]",
                 len(track), track[0].x, track[-1].x, track[0].y, track[-1].y)
        return {"track": [(p.x, p.y, p.pixels_per_mm, p.ts) for p in track]}

    def _track_quality_issue(self, track: list) -> Optional[str]:
        if len(track) < self.MIN_TRACKING_FRAMES:
            return f"only {len(track)} track point(s), need at least {self.MIN_TRACKING_FRAMES}"

        duration_s = track[-1].ts - track[0].ts
        if duration_s < self.MIN_TRACK_DURATION_S:
            return (f"track spans only {duration_s * 1000:.0f}ms, "
                    f"need at least {self.MIN_TRACK_DURATION_S * 1000:.0f}ms")

        ppm = track[-1].pixels_per_mm or track[0].pixels_per_mm
        if ppm > 0:
            dx_mm = (track[-1].x - track[0].x) * self.frame_width / ppm
            dy_mm = (track[-1].y - track[0].y) * self.frame_height / ppm
            displacement_mm = math.hypot(dx_mm, dy_mm)
            if displacement_mm < self.MIN_TRACK_DISPLACEMENT_MM:
                return (f"only {displacement_mm:.1f}mm of net displacement, "
                        f"need at least {self.MIN_TRACK_DISPLACEMENT_MM}mm")

        return None

    @property
    def last_known_position(self):
        return self._last_detection, self._last_detection_ts

    @property
    def stable_ppm(self) -> float:
        """Median ppm from recent WAITING detections."""
        if len(self._ppm_samples) < 3:
            return 0.0
        s = sorted(self._ppm_samples)
        return float(s[len(s) // 2])

    def reset(self):
        self.state = PuttState.IDLE
        self._track = []
        self._still_frames = 0
        self._last_detection = None
        self._last_detection_ts = 0.0
        self._ppm_samples = []
        self._vanish_filter_active = False
        self._vanish_filter_seed_x = 0.0
