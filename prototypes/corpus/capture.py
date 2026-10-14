"""Webcam capture tool. Runs on the Windows PC only (Media Foundation, the physical
webcam).

Per docs/GolfSimVision-Phase0-Harness-Specs.md "P0-1" section 1:
- Never trust requested camera settings -- read back what was actually granted and log it.
- time.perf_counter() immediately after cap.read() returns, before any conversion.
- Manual SPACE-to-save is primary; --auto-trigger (frame-diff) exists only to characterise
  a naive trigger's false-positive rate, not to collect the corpus.

Backend and fps ceiling, found live 2026-09-17 (P0-1 setup, this specific camera):
- cv2.CAP_DSHOW (DirectShow) silently caps this camera at ~30fps no matter what
  width/height/fps is requested (measured from real frame timestamps, not just the
  driver's own readback, which happily reports back whatever was asked for). Confirmed
  even after a clean reboot, ruling out a stuck driver state.
- cv2.CAP_MSMF (Media Foundation) gets genuine ~60fps at both 1280x720 and 1920x1080 --
  MSMF is what this tool now uses. Windows' own camera settings UI confirms 60fps is
  this camera's real hardware ceiling; the 120fps originally assumed in
  docs/GolfSimVision-Architecture-Decisions.md's P0-1 section does not hold for this
  camera and was never previously verified against real hardware capability.
- Under MSMF, cap.set(CAP_PROP_FOURCC, ...) reports failure (returns False) and the
  fourcc readback is unusable -- but real frames still decode fine through cap.read(),
  so this is harmless. FOURCC is still requested for the record (the "actual" dict is
  what matters, never "requested" -- same principle as the fps check below).

The cv2.VideoCapture/imshow/waitKey loop below is the interactive shell -- it can't be
unit tested from here (no display, no physical mat). RingBuffer, clip saving, and camera
readback logging are pure/file-I/O functions and are covered in test_capture.py.
"""

from __future__ import annotations

import argparse
import sys
import threading
import time
from collections import deque
from datetime import datetime, timezone
from pathlib import Path

import cv2
import numpy as np

from corpus import (
    CameraProps,
    ClipMetadata,
    Conditions,
    Frames,
    Geometry,
    Timing,
    append_index_entry,
    frames_dir,
    next_clip_id,
    save_clip,
)
from stats import jitter_report

CAMERA_READBACK_PROPS = {
    "width": cv2.CAP_PROP_FRAME_WIDTH,
    "height": cv2.CAP_PROP_FRAME_HEIGHT,
    "fps": cv2.CAP_PROP_FPS,
    "fourcc": cv2.CAP_PROP_FOURCC,
}

LOGGED_PROPS = {
    "CAP_PROP_EXPOSURE": cv2.CAP_PROP_EXPOSURE,
    "CAP_PROP_AUTO_EXPOSURE": cv2.CAP_PROP_AUTO_EXPOSURE,
    "CAP_PROP_GAIN": cv2.CAP_PROP_GAIN,
    "CAP_PROP_BRIGHTNESS": cv2.CAP_PROP_BRIGHTNESS,
    "CAP_PROP_CONTRAST": cv2.CAP_PROP_CONTRAST,
}


class RingBuffer:
    """Fixed-capacity (timestamp, frame) buffer. Oldest frames drop once full.

    Lock-protected: the capture thread appends while the display/main thread reads
    (latest() for the preview, frames() to save a clip), and deque mutation during
    another thread's iteration would otherwise raise.
    """

    def __init__(self, maxlen: int):
        self._buf: deque = deque(maxlen=maxlen)
        self._lock = threading.Lock()

    def append(self, timestamp: float, frame) -> None:
        with self._lock:
            self._buf.append((timestamp, frame))

    def frames(self) -> list[tuple[float, object]]:
        """(timestamp, frame) pairs in capture order, oldest first."""
        with self._lock:
            return list(self._buf)

    def latest(self) -> tuple[float, object] | None:
        """The most recently appended (timestamp, frame), or None if empty."""
        with self._lock:
            return self._buf[-1] if self._buf else None

    def __len__(self) -> int:
        with self._lock:
            return len(self._buf)

    @property
    def maxlen(self) -> int:
        return self._buf.maxlen


def fourcc_to_str(value: float) -> str:
    """Decode a cv2 CAP_PROP_FOURCC readback (a packed float) back to its 4-char code."""
    v = int(value)
    return "".join(chr((v >> (8 * i)) & 0xFF) for i in range(4))


def open_camera(index: int, width: int, height: int, fps: int) -> tuple[cv2.VideoCapture, dict, dict]:
    """Open the camera via Media Foundation, then read back what was actually granted.

    Returns (capture, requested, actual) so the caller can log both -- a camera that
    silently gives fewer fps than requested must be caught here, not discovered
    downstream. See the module docstring for why CAP_MSMF, not CAP_DSHOW.
    """
    # No FOURCC request here: cv2.CAP_PROP_FOURCC always reports rejection (returns
    # False) under CAP_MSMF for this camera -- see docs/CAMERA_CAPABILITIES.md.
    #
    # Each step below is genuinely slow on this camera/driver -- found live that
    # VideoCapture() and each individual cap.set() can each take ~15s under CAP_MSMF
    # (a real renegotiation delay, not a hang) -- so every step logs before/after with a
    # timestamp. Without this, capture.py looks identical to "stuck" for up to a minute.
    t0 = time.perf_counter()

    def _log(msg: str) -> None:
        print(f"[capture] [{time.perf_counter() - t0:5.1f}s] {msg}", flush=True)

    _log("opening camera (CAP_MSMF) -- this can take up to ~15s on this hardware")
    cap = cv2.VideoCapture(index, cv2.CAP_MSMF)
    _log(f"opened, isOpened={cap.isOpened()}")
    _log("requesting width (can take up to ~15s)")
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, width)
    _log("requesting height (can take up to ~15s)")
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, height)
    _log("requesting fps (can take up to ~15s)")
    cap.set(cv2.CAP_PROP_FPS, fps)
    _log("done setting properties, reading back actual values")

    requested = {"width": width, "height": height, "fps": fps, "fourcc": "(not requested)"}
    actual = {
        "width": int(cap.get(cv2.CAP_PROP_FRAME_WIDTH)),
        "height": int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT)),
        "fps": cap.get(cv2.CAP_PROP_FPS),
        "fourcc": fourcc_to_str(cap.get(cv2.CAP_PROP_FOURCC)),
    }
    return cap, requested, actual


def read_diagnostic_props(cap: cv2.VideoCapture) -> dict:
    return {name: cap.get(prop_id) for name, prop_id in LOGGED_PROPS.items()}


def build_clip_metadata(
    clip_id: str,
    kind: str,
    frames: list[tuple[float, object]],
    requested: dict,
    actual: dict,
    device_name: str,
    diagnostic_props: dict,
    exposure_locked: bool = False,
    exposure_note: str = "",
    geometry: Geometry | None = None,
    conditions: Conditions | None = None,
) -> ClipMetadata:
    """Assemble a ClipMetadata from a ring buffer's frames plus camera diagnostics."""
    timestamps = [t for t, _ in frames]
    t0 = timestamps[0] if timestamps else 0.0
    relative_timestamps = [t - t0 for t in timestamps]
    report = jitter_report(
        [(b - a) * 1000.0 for a, b in zip(relative_timestamps, relative_timestamps[1:])]
    )
    measured_fps = 1000.0 / report["median"] if report["median"] else 0.0
    # Field names match the clip.json example in the harness spec exactly (median/p95/
    # max/jitter_sd) -- gap_count lives only at Timing.gap_count, not duplicated here.
    interval_ms = {
        "median": report["median"],
        "p95": report["p95"],
        "max": report["max"],
        "jitter_sd": report["stdev"],
    }

    return ClipMetadata(
        clip_id=clip_id,
        created_utc=datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        kind=kind,
        camera=CameraProps(
            requested=requested,
            actual=actual,
            device_name=device_name,
            exposure_locked=exposure_locked,
            exposure_note=exposure_note,
            props=diagnostic_props,
        ),
        geometry=geometry or Geometry(),
        conditions=conditions or Conditions(),
        frames=Frames(
            count=len(frames), first_index=0, timestamps_s=relative_timestamps
        ),
        timing=Timing(
            measured_fps=measured_fps,
            interval_ms=interval_ms,
            gap_count=report["gap_count"],
        ),
    )


def save_ring_buffer_as_clip(
    corpus_root: Path,
    ring: RingBuffer,
    kind: str,
    requested: dict,
    actual: dict,
    device_name: str,
    diagnostic_props: dict,
    **kwargs,
) -> ClipMetadata:
    """Save the ring buffer's current contents as a new clip and update the index."""
    frames = ring.frames()
    clip_id = next_clip_id(corpus_root)
    meta = build_clip_metadata(
        clip_id, kind, frames, requested, actual, device_name, diagnostic_props, **kwargs
    )
    out_dir = frames_dir(corpus_root, clip_id)
    out_dir.mkdir(parents=True, exist_ok=True)
    for i, (_, frame) in enumerate(frames):
        gray = frame if frame.ndim == 2 else cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        cv2.imwrite(str(out_dir / f"{i:06d}.png"), gray)
    save_clip(corpus_root, meta)
    append_index_entry(corpus_root, meta)
    return meta


class CaptureWorker:
    """Owns the capture thread: read -> timestamp -> push to ring buffer, nothing else.

    Per docs/GolfSimVision-Phase0-Harness-Specs.md "P0-1" section 1: "Single dedicated
    capture thread. Nothing but read -> timestamp -> push to ring buffer. No processing,
    no encoding, no display work on this thread." All display/keyboard/trigger work runs
    on the caller's thread against the ring buffer instead, so it can never block capture.
    """

    def __init__(self, cap: cv2.VideoCapture, ring: RingBuffer):
        self.cap = cap
        self.ring = ring
        self.error: str | None = None
        self.actual_fps = 0.0
        self._recent_timestamps: deque = deque(maxlen=30)
        self._stop = threading.Event()
        self._thread = threading.Thread(target=self._run, daemon=True)

    def start(self) -> None:
        self._thread.start()

    def stop(self) -> None:
        self._stop.set()
        self._thread.join(timeout=2.0)

    def _run(self) -> None:
        while not self._stop.is_set():
            ok, frame = self.cap.read()
            t = time.perf_counter()
            if not ok:
                self.error = "frame read failed"
                return
            self.ring.append(t, frame)
            self._recent_timestamps.append(t)
            if len(self._recent_timestamps) >= 2:
                dt = self._recent_timestamps[-1] - self._recent_timestamps[0]
                if dt > 0:
                    self.actual_fps = (len(self._recent_timestamps) - 1) / dt


class AutoTrigger:
    """Simple ROI frame-differencing trigger, for baselining false-positive rate only."""

    def __init__(self, threshold: float = 25.0, min_changed_fraction: float = 0.02):
        self._prev = None
        self.threshold = threshold
        self.min_changed_fraction = min_changed_fraction

    def update(self, frame) -> bool:
        """Returns True if this frame looks like motion relative to the previous one."""
        gray = frame if frame.ndim == 2 else cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        if self._prev is None:
            self._prev = gray
            return False
        diff = cv2.absdiff(gray, self._prev)
        self._prev = gray
        changed = np.count_nonzero(diff > self.threshold)
        return (changed / diff.size) > self.min_changed_fraction


def run(args: argparse.Namespace) -> None:
    corpus_root = Path(args.corpus)
    cap, requested, actual = open_camera(args.index, args.width, args.height, args.fps)
    if not cap.isOpened():
        print(f"[capture] failed to open camera index {args.index}", file=sys.stderr)
        sys.exit(1)

    diagnostic_props = read_diagnostic_props(cap)
    print(f"[capture] requested: {requested}")
    print(f"[capture] actual:    {actual}")
    if actual["fps"] < requested["fps"] * 0.9:
        print(
            f"[capture] WARNING: requested {requested['fps']}fps, camera granted "
            f"{actual['fps']:.1f}fps -- this session's data will not reflect the target rate",
            file=sys.stderr,
        )

    ring = RingBuffer(maxlen=int(args.pre_roll * args.fps))
    worker = CaptureWorker(cap, ring)
    worker.start()

    auto_trigger = AutoTrigger() if args.auto_trigger else None
    pending_kind = "valid_putt"
    last_saved: str | None = None
    display_interval = 1.0 / 15  # ~15fps preview, per the harness spec

    def save(kind: str) -> str:
        nonlocal last_saved
        meta = save_ring_buffer_as_clip(
            corpus_root, ring, kind, requested, actual, args.device_name, diagnostic_props,
        )
        last_saved = meta.clip_id
        return meta.clip_id

    print("[capture] SPACE=save  V=mark valid  A=mark adversarial  R=discard last  Q=quit")

    try:
        next_display = time.perf_counter()
        wait_start = time.perf_counter()
        first_frame_seen = False
        next_wait_log = wait_start + 2.0
        while True:
            if worker.error:
                print(f"[capture] {worker.error}, stopping", file=sys.stderr)
                break

            current = ring.latest()
            if current is None:
                now = time.perf_counter()
                if now >= next_wait_log:
                    print(
                        f"[capture] still waiting for the first frame "
                        f"({now - wait_start:.0f}s elapsed) -- camera opened but "
                        f"hasn't delivered a frame yet; Ctrl+C to give up",
                        file=sys.stderr,
                    )
                    next_wait_log = now + 2.0
                time.sleep(0.01)
                continue
            if not first_frame_seen:
                first_frame_seen = True
                print(
                    f"[capture] first frame received after {time.perf_counter() - wait_start:.2f}s"
                )
            _, latest_frame = current

            if auto_trigger and auto_trigger.update(latest_frame):
                clip_id = save("auto_trigger")
                print(f"[capture] auto-trigger fired -> saved {clip_id}")

            now = time.perf_counter()
            if now >= next_display:
                next_display = now + display_interval
                preview = cv2.resize(latest_frame, None, fx=0.5, fy=0.5)
                readout = (
                    f"fps={worker.actual_fps:.1f} fill={len(ring)}/{ring.maxlen} "
                    f"last={last_saved or '-'}"
                )
                cv2.putText(preview, readout, (10, 20), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 0), 1)
                cv2.imshow("capture (preview)", preview)

            key = cv2.waitKey(1) & 0xFF
            if key == ord(" "):
                clip_id = save(pending_kind)
                print(f"[capture] saved {clip_id} (kind={pending_kind})")
            elif key == ord("v"):
                pending_kind = "valid_putt"
                print("[capture] next save marked: valid_putt")
            elif key == ord("a"):
                pending_kind = "adversarial"
                print("[capture] next save marked: adversarial")
            elif key == ord("r") and last_saved:
                print(f"[capture] R pressed -- manually delete {last_saved} if unwanted")
            elif key == ord("q"):
                break
    finally:
        worker.stop()
        cap.release()
        cv2.destroyAllWindows()


def main() -> None:
    parser = argparse.ArgumentParser(description="Webcam putting capture tool (Windows only).")
    parser.add_argument("--corpus", default="corpus", help="path to corpus root")
    parser.add_argument("--index", type=int, default=0, help="camera index")
    parser.add_argument("--width", type=int, default=1280)
    parser.add_argument("--height", type=int, default=720)
    parser.add_argument("--fps", type=int, default=60)  # confirmed hardware ceiling, see module docstring
    parser.add_argument("--pre-roll", type=float, default=4.0, help="ring buffer seconds")
    parser.add_argument("--device-name", default="", help="e.g. 'Logitech Brio'")
    parser.add_argument(
        "--auto-trigger", action="store_true",
        help="save on frame-diff motion, to baseline false-positive rate (not for corpus collection)",
    )
    args = parser.parse_args()
    run(args)


if __name__ == "__main__":
    main()
