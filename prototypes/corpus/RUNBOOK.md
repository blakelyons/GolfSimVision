# P0-1 Corpus Collection Runbook

A session-level guide for actually building the corpus with `capture.py`/`label.py`/
`stats.py`/`replay.py`. `README.md` is the reference for what each tool does; this doc is
the order to run them in, what to physically set up first, and what to do with the
results afterward. **`capture.py` runs on the Windows PC** with the webcam connected;
`label.py`/`stats.py`/`replay.py` can run there too or on any machine that can read the
`corpus/` folder afterward.

## Before you start

- [ ] `pip install -r requirements.txt` done (see that file if plain `pip install` fails
      on this machine — known `distlib` launcher bug, workaround documented there).
- [ ] Webcam mounted overhead, positioned over the mat exactly as it will be in real use
      — camera height and angle changes invalidate the pixel-per-mm assumptions in
      ground truth and in the baseline detector.
- [ ] Two physical marks placed on the mat a **measured** distance apart (400mm is the
      spec's default), both inside the camera's view, for two-gate ground truth.
- [ ] If using the ramp method for repeatable speeds: ramp available with release
      heights marked (10/20/30/45/60cm — see `label.py`'s reference table).
- [ ] At least two ball colors available (white + yellow), plus one with a heavy
      alignment line/logo, per the test matrix.
- [ ] Run `python capture.py --corpus ./corpus --device-name "<your camera>"` once and
      check the printed `requested:`/`actual:` line — confirm the camera actually
      granted MJPG/1280x720/120fps before collecting anything. A silent fallback to
      30fps invalidates the whole session; the tool warns if this happens, but check it.

## Phase 1 — Auto-trigger baseline (~15 min, do first)

Per the harness spec, this characterizes a naive trigger's false-positive rate — it's a
required deliverable in its own right, separate from the manual corpus, and its clips
(tagged `kind=auto_trigger`) don't count toward the 50+/20-adversarial targets below.

```
python capture.py --corpus ./corpus --auto-trigger
```

1. **Provoked (~10 min):** deliberately place/move the ball and your hand in frame the
   way you would during real collection. Note how many `auto_trigger` clips got saved
   vs. how many were actually real putts.
2. **Idle soak (~10 min):** step away, empty mat, nobody present. Ideally zero
   `auto_trigger` saves — note the actual count. This is the spec's "10-minute idle
   clip... false-positive soak test."

Jot both counts down now (e.g. in a scratch note or directly into a draft of
`docs/CAMERA_CAPABILITIES.md`) — you'll want them for the rollup in Phase 5.

## Phase 2 — Manual corpus collection (the bulk of the work, ~1-2 hrs)

Switch to manual mode (no `--auto-trigger`) — `SPACE` after the putt is primary
collection, since the ring buffer already holds it:

```
python capture.py --corpus ./corpus
```

`V`/`A` mark the *next* save as valid/adversarial before you press `SPACE`.

### Valid putts (~30 clips) — press `V` first

| Dimension | Values | Target |
|---|---|---|
| Speed | slow (~2mph), medium (~5mph), firm (~9mph) | 5 each (15 clips) |
| Line | straight, ~3° left, ~3° right | spread across the 15 |
| Ball | white, yellow, heavy alignment line/logo | spread across the 15 |
| Surface | usual mat, + one other if available | mostly usual mat |
| Lighting | ambient; repeat a subset with an added LED panel | subset |

Use the ramp for repeatable speed control if you have one set up — it's more consistent
than putting the same speed by feel, and its heights map directly to `label.py
--record-ramp`.

### Adversarial / non-putt (~20 clips) — press `A` first

Straight from the harness spec's table — every row exists because it broke a prior
implementation in the field:

- [ ] Hand reaches in and places the ball, no putt (**highest severity**)
- [ ] Putter head enters and sweeps through, no ball
- [ ] Putter follow-through immediately after a real putt
- [ ] Two putts in quick succession (**highest severity**)
- [ ] Ball retrieved and re-placed
- [ ] Feet / shadow crossing the zone
- [ ] Ball rolls in from off-frame
- [ ] Ball stops inside the zone
- [ ] Hard direct sunlight / bright hotspot (skip if you can't reproduce this)
- [ ] Ball partially occluded at the start

Aim for ~2 clips per row to reach ~20.

After a session, sanity-check counts:
```
python stats.py --corpus ./corpus
```

## Phase 3 — Ground truth labelling

```
# Two-gate: frame-step to find the frames where the ball crosses your two marks.
python label.py --corpus ./corpus clip_0001
# then record it:
python label.py --corpus ./corpus clip_0001 --record-two-gate 112 211 400.0 --hla -1.2

# Or, for a ramp-released clip, skip frame-stepping entirely:
python label.py --corpus ./corpus clip_0001 --record-ramp 30
```

Target **20+ clips with two-gate (or ramp) ground truth** — apply it to `valid_putt`
clips (a rolling ball is what the math needs); the spec calls two-gate "tedious, so apply
it to ~20 clips, not all 50."

Target **15+ clips with tier-2 per-frame labels** (open a clip with no `--record-*` flag,
click the ball each frame, `Q` to save). Prioritize `valid_putt` clips and any
adversarial clip where a ball is actually visible (e.g. "ball rolls in from off-frame") —
skip labelling clips with no ball in frame at all (e.g. "hand reaches in, no putt").

## Phase 4 — Jitter characterization and baseline scoring

```
python stats.py --corpus ./corpus --histogram
python replay.py --detector detectors.cam_putting_baseline --corpus ./corpus --report out/baseline.json
```

Review both outputs — this is the point of the whole corpus. The jitter numbers
(median/p95/max/stdev, gap count) tell you whether frame-interval noise is a real
accuracy floor or just noise a least-squares fit averages out (spec's rule of thumb: a
few tenths of a ms of stdev with rare gaps is fine; several ms with frequent gaps is not).
The scorecard's false-positive count matters most (ADR-0007: precision over recall).

## Phase 5 — Roll the results up

1. Write **`docs/CAMERA_CAPABILITIES.md`** from Phase 4's `stats.py` output plus Phase
   1's auto-trigger false-positive counts — this doesn't exist yet and is a named exit
   criterion.
2. Check the exit criteria in `README.md`: 50+ clips / 20+ adversarial with metadata ✓,
   20+ two-gate-labelled ✓, 15+ tier-2-labelled ✓, jitter write-up ✓, `replay.py` scoring
   the baseline end-to-end ✓.

## Known gap: geometry/conditions aren't wired to the CLI yet

`clip.json`'s `geometry` (camera height, FOV, mount) and `conditions` (lighting,
surface, ball) sections default empty — `capture.py` has no `--camera-height`/
`--lighting`/etc. flags yet, only `--device-name`. Since these are constant for an entire
session, the cheapest fix right now is a one-off batch edit after each session rather
than a CLI change:

```python
from pathlib import Path
from corpus import Conditions, Geometry, load_clip, save_clip, read_index

root = Path("corpus")
for entry in read_index(root):
    meta = load_clip(root, entry["clip_id"])
    meta.geometry = Geometry(camera_height_in=31.5, camera_fov_deg=78.0, mount="overhead, ball travels left to right")
    meta.conditions = Conditions(lighting="room ambient", surface="practice mat, medium", ball="white Titleist ProV1")
    save_clip(root, meta)
```

Adjust the values per session and re-run after each capture session, before moving on to
labelling.
