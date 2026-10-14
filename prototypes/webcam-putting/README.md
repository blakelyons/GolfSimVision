# Live webcam putting -> GSPro

A live pipeline: webcam -> ball detection -> putt speed/HLA -> GSPro. This is
where GolfSimVision's Phase 0 spikes come together into something you can
actually putt at:

- Camera capture reuses `prototypes/corpus/capture.py`'s `open_camera()` --
  the CAP_MSMF backend and confirmed 60fps ceiling from P0-1's setup work.
- Ball detection (`detection/`) is V1 (SimLinkPutting)'s YOLO detector + zone
  state machine + putt speed/HLA calculator, ported here largely unmodified
  (see each file's docstring for exactly what changed).
- GSPro integration reuses `prototypes/gspro-harness/protocol.py`'s
  `GSProDirect`/`GSProCodec` -- P0-2's already-tested OpenConnect client.

## Setup

```
pip install -r requirements.txt
```

If plain `pip install` fails with `ValueError: Unable to find resource t64.exe
in package pip._vendor.distlib`, see the workaround documented in
`prototypes/corpus/requirements.txt` -- same known issue on this machine,
extended here to cover ultralytics' full dependency tree (torch etc.), not
just numpy/opencv.

## Before you start

- [ ] GSPro running with an **active round**. OpenConnect silently ignores
      shots with no round active -- no error, no response at all.
- [ ] The **putter selected as GSPro's current club, in GSPro's own UI**.
      OpenConnect has no message to select a club programmatically -- GSPro
      infers "this is a putt" purely from whatever club is currently
      selected there. This script watches GSPro's club-change responses and
      prints a warning if it looks wrong, but cannot fix it for you.
- [ ] Camera mounted overhead, ball's resting position in the **left ~30%**
      of frame, travelling **left to right** -- this matches V1's own
      start-zone/gateway convention (`--start-zone`/`--gateway-x` defaults).
      If your camera is mounted differently, override those flags.

## Run it

```
python live_putt.py
```

Add `--dry-run` to test detection without a GSPro connection (prints
detected putts instead of sending them). Add `--headless` to skip the
preview window.

## Known limitations (real, not yet resolved)

- **Grayscale-vs-color is a non-issue here** (unlike the P0-1 corpus, which
  stores grayscale PNGs to save disk space) -- this pipeline runs on live
  color frames straight from the camera, matching what the YOLO model was
  actually trained on. Nothing to adapt.
- **CPU-only inference speed is untested against a fast real putt.** No GPU
  is required (falls back automatically), but a slow-enough YOLO inference
  loop could mean fewer effective detections per real putt than the camera's
  60fps ceiling suggests. If putts through the gateway aren't being
  detected reliably, this is the first thing to profile.
- **GSPro send pacing is a conservative fixed floor** (`MIN_SHOT_INTERVAL_S =
  2.0` in `live_putt.py`), not adaptive to GSPro's actual `202 "ready"`
  signal (which can arrive 5-15s after a shot) -- chosen so a real putting
  session isn't throttled to one putt per ~15s, at the cost of not being
  the theoretically safest possible pacing. Revisit if shots start getting
  dropped in practice.
- **Zone/gateway coordinates are V1's untouched defaults.** They almost
  certainly need recalibrating to your actual camera framing -- watch the
  preview window's green start-zone box and orange gateway line against
  where your ball and strip actually sit, and adjust `--start-zone`/
  `--gateway-x` accordingly.
