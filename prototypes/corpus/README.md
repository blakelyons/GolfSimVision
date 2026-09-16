# P0-1 — Putting Corpus & Replay Harness

Phase 0 throwaway tooling (ADR-0006) — none of this ships. It exists to produce the
corpus every later detection decision is scored against, and to measure frame-interval
jitter and adversarial-case detection rate, per
`docs/GolfSimVision-Phase0-Harness-Specs.md` §P0-1. See `SPEC.md` for the implementation
decisions behind this layout.

## Where things run

| Tool | Machine | Why |
|---|---|---|
| `capture.py` | **Windows PC**, webcam connected | DirectShow |
| `label.py`, `replay.py`, `stats.py` | either | offline analysis over a corpus |

## Prerequisites

```
pip install -r requirements.txt
```

installs `numpy` and `opencv-python`. If plain `pip install` fails with a `distlib`
launcher error, see the workaround documented at the top of `requirements.txt`.

## The shared data model — `corpus.py`

`capture.py`, `label.py`, `replay.py`, and `stats.py` all read/write clips through
`corpus.py` instead of touching JSON directly, so the on-disk shape (matching the
`clip.json` example in the harness spec) lives in exactly one place:

- `save_clip(root, meta)` / `load_clip(root, clip_id)` — one clip's `clip.json`.
- `append_index_entry(root, meta)` / `read_index(root)` — the corpus-wide `index.jsonl`.
- `next_clip_id(root)` — smallest unused `clip_NNNN`.

## 1. `capture.py` — record clips (Windows PC)

```
python capture.py --corpus ./corpus --index 0 --device-name "Logitech Brio"
```

Opens the camera with MJPG set first (required at 120fps), logs requested vs. actual
width/height/fps/fourcc, and warns if the camera silently granted a lower fps than
requested. A dedicated `CaptureWorker` background thread does nothing but read →
timestamp (`time.perf_counter()`, immediately after `cap.read()`) → push to a ring buffer
sized by `--pre-roll` seconds (default 4s). The main thread runs the ~15fps preview
window (downscaled, with a readout: rolling actual fps, ring fill level, last-clip
filename) and keyboard handling against the ring buffer's latest frame — it never touches
the capture thread and so can never gate or block it.

Keys, while the preview window has focus:

| Key | Action |
|---|---|
| `SPACE` | Save the ring buffer as a clip |
| `V` | Mark the next save as a **valid putt** |
| `A` | Mark the next save as **adversarial / non-putt** |
| `R` | (manual) delete the last-saved clip's folder if unwanted |
| `Q` | Quit |

`--auto-trigger` runs simple frame-differencing and saves on any detected motion. This
exists to baseline a naive trigger's false-positive rate — **not** to collect the corpus;
manual `SPACE` after the putt is the primary collection method.

Each save writes `clip_NNNN/frames/000000.png ...` (grayscale PNG, one file per frame),
`clip_NNNN/clip.json`, and appends a summary line to `corpus/index.jsonl`.

## 2. `label.py` — ground truth and tier-2 labels (either machine)

```
# Two-gate manual timing: mark the frames where the ball crosses two marks a measured
# distance apart, then record the speed they imply.
python label.py clip_0001 --record-two-gate 112 211 400.0 --hla -1.2

# Ramp release height: v = sqrt(10 * g * h / 7) for a solid sphere rolling without slip.
python label.py clip_0001 --record-ramp 30

# No --record-* flags: opens the interactive frame-stepper (arrow keys to step, click to
# mark the ball centre per frame for tier-2 labels, G to mark a gate-crossing frame,
# Q to quit and save whatever was marked).
python label.py clip_0001
```

Both ground-truth methods write into `clip_0001/clip.json`'s `ground_truth` section.
Tier-2 per-frame labels (ball centre per frame) are written to
`clip_0001/labels_tier2.json` and flip `clip.json`'s `labels_tier2` flag.

## 3. `replay.py` — score a detector against the corpus (either machine)

```
python replay.py --detector detectors.cam_putting_baseline --corpus ./corpus --report out/baseline.json
```

`--detector` is a dotted module path exposing a `create_detector()` factory that returns
an object implementing the `Detector` protocol (`reset()`, `push_frame(t, frame) ->
Optional[PuttMeasurement]`). Frames are fed with their **recorded** timestamps, never
wall-clock and never an assumed constant interval.

Scorecard, printed and optionally written as JSON:

| Metric | Definition |
|---|---|
| `true_positives` | valid-putt clips producing exactly one measurement |
| `false_negatives` | valid-putt clips producing none |
| `false_positives` | adversarial clips producing any measurement — **the number that matters most** (ADR-0007: precision over recall) |
| `double_fires` | any clip producing more than one measurement |
| `speed_error_pct` / `hla_error_deg` | mean/median/p95 absolute error vs. ground truth, over clips that fired exactly once and have ground truth |
| `track_points` | min/max/mean points per measurement |
| `rejections` | count by reason code, if the detector exposes a `rejections: list[str]` attribute |

### The baseline detector — `detectors/cam_putting_baseline.py`

A port of `cam-putting-py`'s approach (contour tracking + a ball-radius-to-mm scale +
two-gate timing), per the harness spec: *"we beat the existing free tool by X"* is the
only meaningful benchmark available. One deliberate adaptation from the original: since
`capture.py` stores grayscale-only PNGs (harness spec §1), there's no hue/saturation
channel to run true HSV masking on. The original's "white ball" HSV range is, in
practice, a brightness threshold — so this port thresholds directly on grayscale value,
which is the same selectivity for the one ball color a grayscale corpus can support. See
the module docstring for the full rationale.

Add new detectors as new files under `detectors/`, each exposing its own
`create_detector()` — `replay.py` never needs to change.

## 4. `stats.py` — corpus summary + jitter report (either machine)

```
python stats.py --corpus ./corpus --histogram
```

Reports clip counts (by kind, with-ground-truth, with-tier2-labels) and a corpus-wide
frame-interval jitter report: median/p95/max/stdev (ms) and a count of gaps (intervals
> 1.5x the median). This is a first-class Phase 0 finding in its own right — the
eventual write-up goes in `docs/CAMERA_CAPABILITIES.md`, built from real capture
sessions.

## Testing

```
python -m unittest discover
```

53 tests across `test_corpus.py`, `test_stats.py`, `test_capture.py`, `test_label.py`,
`test_replay.py`, and `test_cam_putting_baseline.py`. Everything interactive
(`cv2.VideoCapture`, `cv2.imshow`, keyboard handling in `capture.py`'s and `label.py`'s
`main()`/`run()`/`_run_stepper()` shells) is untested here by necessity — no display, no
physical mat, no hands on a keyboard from this environment. Everything else (ring buffer,
clip saving, ground-truth math, the detector protocol, the baseline detector's tracking
logic against synthetic frames) is covered.

## Exit criteria

Unchanged from `docs/GolfSimVision-Phase0-Harness-Specs.md` §P0-1 "6. Exit criteria" —
these require real capture sessions on the Windows PC and are Blake's manual work once
these tools exist:

- 50+ clips, ≥ 20 adversarial, with metadata and timestamps.
- 20+ clips with two-gate ground truth.
- 15+ clips with tier-2 per-frame ball labels.
- A written frame-interval jitter characterisation → `docs/CAMERA_CAPABILITIES.md`.
- `replay.py` scoring the cam-putting-py baseline end-to-end.
