# Camera Capabilities (P0-1)

Findings about the actual webcam hardware/driver behavior on Blake's Windows PC,
established while setting up `prototypes/corpus/capture.py` for real corpus collection.
Phase 0 finding, not a vendor spec. This file will grow with the jitter characterization
and auto-trigger false-positive numbers once real capture sessions run (RUNBOOK.md
Phase 5); for now it covers the backend/fps-ceiling finding that blocked getting any
valid session started at all.

## Real fps ceiling: 60fps, not the 120fps originally assumed

`docs/GolfSimVision-Architecture-Decisions.md`'s P0-1 section describes "Blake's
existing 720p120 webcam" — that assumption was never verified against real hardware
capability before this session, and does not hold for the camera actually in use
(a Logitech Brio): confirmed live 2026-09-17 via Windows' own camera settings UI
(Settings → Bluetooth & devices → Cameras), whose selectable-mode list tops out at
**60fps** (1080p or 720p). 120fps is not an option Windows itself offers for this
camera, regardless of what any capture library requests.

## Backend matters: `CAP_DSHOW` silently caps this camera at ~30fps; `CAP_MSMF` does not

Diagnosed with a throwaway probe script (measuring real inter-frame intervals from
`time.perf_counter()` timestamps around `cap.read()`, never trusting `cap.get(CAP_PROP_FPS)`
alone, since that just echoes back whatever was last set):

| Backend | Requested | Reported back | Measured (real) |
|---|---|---|---|
| `CAP_DSHOW` | 1280x720@60 | 1280x720@60.0, fourcc YUY2 | ~32ms interval (~31fps) |
| `CAP_DSHOW` | 1920x1080@60 | 1920x1080@60.0, fourcc YUY2 | ~32ms interval (~31fps) |
| `CAP_MSMF` | 1280x720@60 | 1280x720@60.0 | ~15.4ms interval (~65fps) |
| `CAP_MSMF` | 1920x1080@60 | 1920x1080@60.0 | ~15.9ms interval (~63fps) |
| `CAP_MSMF` | 1280x720@30 (sanity check) | 1280x720@30.0 | ~32ms interval (~31fps) |

`CAP_DSHOW` (DirectShow) reports back whatever width/height/fps was requested — even
`cap.set()` calls return `True` — but the real, measured frame rate never exceeds ~31fps
regardless of what's requested. This was confirmed even after a clean reboot, ruling out
a stuck driver/negotiation state rather than a genuine backend limitation. `CAP_MSMF`
(Media Foundation) gets a real, measured ~60-65fps at both 1080p and 720p — the 30fps
sanity-check request on the same backend correctly measured back at ~31fps, confirming
the measurement methodology itself is trustworthy and the >2x difference at 60fps is real.

**`capture.py` now uses `CAP_MSMF`, not `CAP_DSHOW`, and defaults to `--fps 60`.**

## FOURCC/MJPG under MSMF

Under `CAP_MSMF`, `cap.set(CAP_PROP_FOURCC, ...)` reports failure (`False`) and the
fourcc readback is unusable garbage — but real frames still decode correctly through
`cap.read()`, so this is harmless. The original assumption that MJPG is "almost
certainly mandatory" at high fps (`docs/GolfSimVision-Phase0-Harness-Specs.md` §P0-1)
was reasoned from uncompressed-YUY2 bandwidth math at 720p**120** (221 MB/s, beyond
practical USB throughput) — at the real ceiling of 720p**60** (~110 MB/s) that pressure
is roughly halved, and MSMF is in practice getting a real 60fps without us ever
successfully forcing MJPG. Exactly what codec MSMF is actually using isn't confirmed
(fourcc readback is unusable under this backend) — a gap, not a claim that it's
confirmed-uncompressed.

## Contributing environmental factors ruled in/out along the way

- **USB cable mattered.** An earlier USB 2.0-class cable capped real throughput to
  ~30fps regardless of settings (consistent with the DSHOW numbers above, though DSHOW
  stayed capped at ~30fps even after swapping to a USB 3.0 cable -- the cable and the
  backend turned out to be two separate contributing factors, not one explaining the
  other).
- **Exposure/shutter speed** was suspected as another possible fps ceiling (a long
  exposure time puts a hard floor under the frame interval independent of any requested
  mode) — worth keeping in mind if fps ever regresses again, though the backend switch
  alone resolved it this time.
- **A reboot was required** at one point to clear a stuck state where `cap.set()` for
  width/height reported success but the camera stayed stuck reporting back whatever
  resolution (2560x1440) it happened to already be in — a driver/OS-level staleness
  after the cable swap, not something any capture-side code change fixed.
