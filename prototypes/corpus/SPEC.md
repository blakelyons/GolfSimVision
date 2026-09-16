# P0-1 — Putting Corpus & Replay Harness — Implementation Spec

Source of truth for requirements: `docs/GolfSimVision-Phase0-Harness-Specs.md` §"P0-1".
This file only records the implementation decisions needed to turn that spec into tickets —
it does not restate anything the harness spec already settles.

Phase 0 throwaway tooling (ADR-0006) — none of this ships or gets ported. Unlike P0-2's
`protocol.py`, nothing here is written with porting in mind.

## Environment note

`pip install` on this machine fails during console-script generation (`pip._vendor.distlib`
is missing its launcher `.exe` resources — looks AV/EDR-stripped, not a project issue).
`pip download` + extracting the wheel directly into site-packages works around it; see
`requirements.txt` for the exact commands. `numpy` and `opencv-python` are installed this way.

## Decisions

1. **Shared data model — `corpus.py`.** `capture.py`, `label.py`, `replay.py`, and `stats.py`
   all read/write the same `clip.json` shape and the same `index.jsonl` shape. Rather than
   let four tools each hand-roll that JSON (drift risk, and Duplicated Code on the standards
   axis), `corpus.py` owns it: dataclasses matching the harness spec's `clip.json` example
   field-for-field, plus `load_clip`/`save_clip`/`append_index_entry`/`read_index`. Every
   other file imports from here instead of touching JSON directly.

2. **Testable core, untestable shell.** `capture.py` and `label.py` are interactive
   (webcam + `cv2.imshow` + keyboard), which I can't drive from here — no display, no
   physical mat, no hands on a keyboard. So each tool is split: a thin `main()` shell that
   owns the `cv2` I/O loop (not unit tested, only manually run by Blake on the Windows PC),
   and pure functions/classes around it that are (ring buffer, clip-save/naming, gate-speed
   and ramp-height math, metadata assembly). `replay.py` and `stats.py` have no interactive
   shell at all — they're fully testable end to end against synthetic fixtures.

3. **`RingBuffer`** — `collections.deque(maxlen=N)` wrapped in a tiny class in `capture.py`
   (`append`, `frames()`, `len`), N computed from `pre_roll_seconds * fps`. Pure Python,
   direct unit tests.

4. **Detector Protocol lives in `replay.py`**, per the harness spec's `Protocol` class. The
   baseline detector (a port of `cam-putting-py`'s HSV + two-sample gate approach) lives in
   `detectors/cam_putting_baseline.py`, one file per detector so `--detector module.path`
   (`importlib`) stays simple and new detectors never touch `replay.py`.

5. **CLI framework: stdlib `argparse`** for all four tools, consistent with `harness.py` in
   P0-2 and with "no new dependencies beyond what the harness spec already names" (cv2,
   numpy).

6. **Tests: stdlib `unittest`**, same as P0-2, one `test_<module>.py` per module.

## Folder layout

```
prototypes/corpus/
├── SPEC.md
├── README.md
├── requirements.txt        # numpy, opencv-python + the pip-workaround note
├── corpus.py                # clip.json / index.jsonl schema + read/write
├── capture.py                # webcam capture tool (Windows PC only)
├── label.py                  # frame-stepper: two-gate + ramp ground truth, tier-2 labels
├── replay.py                  # Detector Protocol, scorecard, CLI
├── stats.py                   # jitter report + corpus summary
├── detectors/
│   ├── __init__.py
│   └── cam_putting_baseline.py   # ported HSV+gate detector, the benchmark
├── test_corpus.py
├── test_capture.py
├── test_label.py
├── test_replay.py
├── test_stats.py
└── corpus/                    # output, gitignored (created at runtime)
```

## Tickets

1. `corpus.py` — dataclasses + `load_clip`/`save_clip`/index I/O, tests.
2. `stats.py` — jitter stats (median/p95/max/sd/gap count) + text histogram + CLI, tests.
3. `capture.py` — `RingBuffer`, camera-open-with-readback-logging, clip-save/naming,
   keyboard-triggered main loop, `--auto-trigger` frame-diff mode. Tests for everything
   except the `cv2.VideoCapture`/`imshow` shell.
4. `label.py` — two-gate speed math, ramp-height formula, tier-2 label storage, frame-stepper
   shell. Tests for the math and the clip.json read-modify-write.
5. `replay.py` + `detectors/cam_putting_baseline.py` — Detector Protocol, scorecard
   (TP/FN/FP/double-fire, speed/HLA error percentiles, rejections), baseline detector ported
   from `cam-putting-py`. Tests against synthetic corpora built with `corpus.py`.
6. `README.md` — prerequisites, how to run each tool (and on which machine), corpus layout,
   how the 50+/20-adversarial/20-two-gate/15-tier2 targets map to workflow.

## Exit criteria

Unchanged from `docs/GolfSimVision-Phase0-Harness-Specs.md` §P0-1 "6. Exit criteria" — those
targets require real capture sessions on the Windows PC with the mat and camera, which is
Blake's manual work once these tools exist, not something this implementation task produces.
