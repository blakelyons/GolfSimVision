# Phase 0 — Progress Log

Running log of what was built, how to run it, what was measured, and what surprised us.
Newest entries on top.

---

## 2026-09-16 — P0-1: RUNBOOK.md

**What happened:** Added `prototypes/corpus/RUNBOOK.md`, mirroring P0-2's RUNBOOK: a
physical-setup checklist, five phases (auto-trigger false-positive baseline first, since
it's a standalone deliverable and its clips shouldn't count toward the 50+/20-adversarial
targets; manual valid-putt + adversarial collection; ground-truth labelling; jitter
characterization + baseline detector scoring; rollup into `docs/CAMERA_CAPABILITIES.md`).

**Real gap flagged, not silently worked around:** `capture.py` has no CLI flags for
`clip.json`'s `geometry`/`conditions` sections (camera height, mount, lighting, surface,
ball) — only `--device-name`. Rather than change the CLI now, the runbook documents a
copy-pasteable batch-edit snippet using `corpus.py`'s `load_clip`/`save_clip` to backfill
these once per session, since they're constant within a session. Doc-only change, no
code affected.

---

## 2026-09-16 — P0-2: RUNBOOK.md

**What happened:** `README.md` documents each of the 20 experiments individually but not
the order to run them in or what to do with the results, so added
`prototypes/gspro-harness/RUNBOOK.md`: a prerequisites checklist, four phases grouped by
what state GSPro needs to be in (menu-only → active round, normal play → active round,
deliberately-abnormal input → connection-drop/resilience last), and a rollup step
describing what goes into `docs/GSPRO_OPENCONNECT.md` and `docs/GSPRO_PUTTING.md` once
every experiment's `NOTES.md` is filled in. Edge-case experiments (13/14/19/20, which
probe malformed input and error responses) and the reconnect experiment (16) are
deliberately sequenced last, so a connection drop there doesn't cost data from earlier
experiments. Doc-only change, no code affected.

---

## 2026-09-16 — P0-1: corpus.py, capture.py, label.py, replay.py, stats.py

**What happened:** Built all of P0-1 in one pass (`prototypes/corpus/`), following the
same `SPEC.md`-then-tickets pattern used for P0-2. Skipped a `/grilling` interview first
since the harness spec already answers nearly every implementation question in detail;
`SPEC.md` records the handful of decisions it doesn't (shared `corpus.py` data model,
testable-core/untestable-shell split for the two interactive tools, detector-as-plugin
layout for `replay.py`).

- `corpus.py` — dataclasses matching the harness spec's `clip.json` example field-for-
  field, plus `save_clip`/`load_clip`/`append_index_entry`/`read_index`/`next_clip_id`.
  Every other tool reads/writes clips through this instead of touching JSON directly.
- `stats.py` — jitter report (median/p95/max/stdev/gap-count) + text histogram + corpus
  summary CLI.
- `capture.py` — webcam capture tool. `RingBuffer`, camera-open-with-readback-logging
  (MJPG set before width/height/fps, actual settings read back and compared against
  requested), clip-save/naming, `--auto-trigger` frame-diff mode. The `cv2.VideoCapture`/
  `imshow`/keyboard loop is an untested interactive shell around otherwise-tested pure
  functions — no display or physical mat available from here to drive it.
- `label.py` — two-gate manual timing (`speed = gate_distance_mm / dt`) and ramp-release
  timing (`v = sqrt(10*g*h/7)`) ground truth, tier-2 per-frame label storage, plus an
  untested interactive frame-stepper shell for the same reason as `capture.py`.
- `replay.py` + `detectors/cam_putting_baseline.py` — the `Detector` protocol
  (`reset()`/`push_frame(t, frame) -> Optional[PuttMeasurement]`), full scorecard (TP/FN/
  FP/double-fire, speed/HLA error percentiles vs. ground truth, track-point distribution,
  rejections by reason code), and a baseline detector ported from `cam-putting-py`'s
  contour-tracking + ball-radius-scale + two-gate-timing approach.
- `README.md` — how to run each tool and on which machine, the scorecard field
  reference, testing notes, unchanged exit criteria.

**Spec tension flagged, not silently resolved:** the harness spec says `capture.py`
stores **grayscale-only** PNGs (§1), but also says the baseline detector is "a straight
port of cam-putting-py's approach (**HSV** + two-sample gate + ball-radius scale)" (§5).
Grayscale has no hue/saturation channel, so true HSV masking isn't possible against this
corpus. Resolved by thresholding on grayscale brightness instead — functionally the same
selectivity as the original's "white ball" HSV range (which is itself mostly a brightness
condition) and the only ball color a grayscale corpus can support. Documented in the
detector's module docstring and in `README.md`, not silently patched over.

**Environment note:** `pip install` on this machine fails during console-script
generation (`pip._vendor.distlib` is missing its launcher `.exe` resources — looks
AV/EDR-stripped). Worked around by `pip download`-ing `numpy`/`opencv-python` and
extracting the wheels directly into site-packages. Documented in `requirements.txt` in
case it recurs.

Suite: 53/53 (`python -m unittest discover`).

**Code review fixes** (two-axis, same pattern as P0-2 — reviewed as new code since
nothing was committed yet):

- **Spec axis, real gap:** `capture.py`'s `run()` did `cap.read()` → `cv2.imshow()` →
  `cv2.waitKey()` all in one synchronous loop — display work sat directly on the capture
  path, contradicting the harness spec's "Single dedicated capture thread... No
  processing, no encoding, no display work on this thread" and "preview must never gate
  or block capture." There was also no rolling-FPS/ring-fill/last-clip readout at all.
  Fixed: added a `CaptureWorker` background thread that does nothing but
  read→timestamp→ring-append; the main thread now owns the ~15fps preview (with the
  required readout) and keyboard handling against `RingBuffer.latest()`, so it can never
  block capture. `RingBuffer` gained a lock (two threads touch it now) and a `latest()`
  method.
- **Spec axis, real bug:** `capture.py` stored `jitter_report()`'s raw dict verbatim as
  `clip.json`'s `interval_ms`, which uses `"stdev"` and duplicates `gap_count` inside it —
  the harness spec's own `clip.json` example uses `{"median","p95","max","jitter_sd"}`
  with no nested `gap_count`. Fixed: `build_clip_metadata` now maps to the exact spec
  field names.
- **Spec axis, partial:** `replay.py`'s `track_points` scorecard metric was only
  min/max/mean; the spec asks for a "distribution of points per measurement." Added a
  frequency-histogram `counts` field alongside min/max/mean.
- **Standards axis, judgement calls fixed:** deduped `capture.py`'s two near-identical
  `save_ring_buffer_as_clip(...)` call sites (auto-trigger vs. `SPACE`) into one `save()`
  closure, and `stats.py`'s two independent interval-gathering loops (`corpus_summary`
  vs. `main --histogram`) into one `all_intervals_ms()` helper.
- **Standards axis, judgement calls left alone:** mm/s→mph done two different ways
  between `label.py` and the ported baseline detector (detector's shape is deliberately
  preserved for traceability to the original), and the `(requested, actual, device_name,
  diagnostic_props)` data clump threaded through `capture.py` — both minor, Phase-0-
  throwaway-appropriate, not worth the abstraction.

Suite now 58/58. `README.md` updated to describe the real capture/display-thread split.

---

## 2026-09-16 — P0-2 ticket 6: README.md (final ticket)

**What happened:** Added `README.md` covering prerequisites (stdlib-only Python, GSPro
running with OpenConnect active before `connect`/a scenario), how to run the REPL and a
single scenario (`python harness.py --scenario exp03_readiness_true`), the full REPL
command reference table, a table of all 20 scenarios with their questions pulled from the
scenario files themselves (kept in sync by construction, not retyped by hand), how to run
`python -m unittest`, and the unchanged exit criteria from the Harness Specs doc.

This was the last of the 6 tickets in `SPEC.md`. Per the `/implement` skill: ran the full
suite once more (36/36), then `/code-review` (two-axis: standards + spec), then fixed what
it found before committing everything under `prototypes/` to `main`.

**Code review fixes** (spec axis caught real gaps, since nothing was committed yet to diff
against — reviewed the whole directory as new code):
- `protocol.py`'s 64KB resync branch cleared the buffer silently; SPEC.md's framing
  section says to log a warning first. Added one (stderr).
- SPEC.md lists "buffer > 64KB → caller's resync path is exercised" as a required test;
  the existing test only called `find_json_end()` directly, not the real `_recv_loop`.
  Added `test_recv_loop_resyncs_after_over_64kb_with_no_complete_frame`, driving a real
  `GSProDirect` against `FakeGSProServer` and confirming a valid frame still arrives after
  the oversized garbage is cleared.
- `exp13_shotnumber_abuse.py`'s matrix row asks for ShotNumber values 2³¹ and 2³¹+1; it
  was sending 2³¹-1 (a valid boundary, not abuse) and 2³¹. Fixed to send both real
  overflow values.
- `GSProCodec.build_putt`/`build_shot` restated `ShotDataOptions`'s own field defaults
  verbatim (standards axis: mild Duplicated Code) — collapsed to `ShotDataOptions()`.

Suite now 37/37 (added the resync test). Nothing else the reviews found needed changing —
the 20 `exp*.py` files' shared shape was judged not to be duplication (a thin, documented
contract, not copy-pasted logic), and `test_harness.py`/`test_scenarios.py` existing
outside SPEC.md's file list was judged benign scope (real coverage for files SPEC.md does
prescribe).

---

## 2026-09-16 — P0-2 ticket 5: scenarios/ (20 experiment scripts)

**What happened:** Added `scenarios/` with one file per row of the Harness Specs'
20-experiment matrix (`exp01_baseline_chatter.py` … `exp20_contains_balldata_false.py`),
each exposing the contract `harness.py` already calls: a module-level `QUESTION` string
and `run(client: GSProDirect) -> None`.

`scenarios/__init__.py` holds two helpers shared by (almost) every scenario, since the
contract only passes `client`, not a codec:
- `make_codec(client)` — builds a `GSProCodec` bound to that client's `next_shot_number`.
- `connect_and_wait(client, timeout=10.0)` — calls `client.connect()` then blocks until
  `is_connected()` or timeout. Scenarios run mostly unattended, so they can't rely on a
  human watching for the `[status] CONNECTED` line the way the REPL can (this is the same
  async-connect timing behavior found during ticket 4's smoke test, just handled
  differently here).

Each scenario follows the experiment's own procedure from the matrix: readiness
send-and-screenshot loops (2–4), passive-observation prompts for GSPro-side actions like
club changes or playing a hole (5–6), `GSProCodec.build_putt`/`build_shot` sweeps
(7–13, 15, 20), hand-built raw JSON for cases the codec can't express — swapped
`APIversion`/`APIVersion` key casing (14) and a payload missing required `BallData`
fields plus outright malformed JSON (19), a `client.drop()` + reconnect round-trip (16),
and prompts for human-driven GSPro state (no round active, 17). Exp 10 imports
`rolling_spin_rpm` directly to report the computed value in its printed output rather
than hiding it inside `build_putt`'s default.

Backed by `test_scenarios.py`: structural coverage only (no GSPro or socket needed) —
confirms all 20 modules exist, each has a non-empty `QUESTION` string, and each exposes a
`run` callable with exactly one parameter named `client`. This is deliberately shallow;
the scenarios' actual behavior can only be verified by running them against real GSPro,
which is manual work for a later session, not something these tests attempt. Full suite
now 36/36 (34 + 2 new).

**Not yet done:** ticket 6 (`README.md`), then the full suite once more, `/code-review`,
and committing all of P0-2 to `main`.

---

## 2026-09-15 — P0-2 ticket 4: harness.py (REPL + scenario runner)

**What happened:** Added `harness.py`: CLI (`--host`, `--port`, `--device-id`, `--units`,
`--scenario`), `Transcript` (writes `transcript.log` in V1's `HH:MM:SS.mmm  GSPro >>/<<
{...}` format plus parallel `transcript.jsonl`), `make_session_dir()`/`write_notes()`,
`parse_kv()`/`parse_bool()`, and the `Harness` class wiring one `GSProDirect` + one
`GSProCodec` together with all 11 REPL commands (`connect`, `disconnect`, `drop`,
`heartbeat`, `putt`, `shot`, `raw`, `shotnum`, `watch`, `status`, `help`, `quit`/`exit`).
`--scenario NAME` imports `scenarios.NAME`, pre-seeds `NOTES.md` with its `QUESTION`,
and calls `run(client)`.

Added `send_raw(bytes)` to `GSProDirect` (protocol.py) so the REPL's `raw` command can
bypass `serialize_payload` without reaching into the transport's private socket/lock;
refactored `send()` to call it, no behavior change (existing tests still pass).

Backed by `test_harness.py`: 6 tests covering `parse_kv`, `parse_bool`, and
`Transcript`'s on-disk format (both files, direction markers, JSON shape) — no GSPro or
socket needed. Full suite now 34/34.

**Manually smoke-tested** (not part of the committed test suite — the REPL's dispatch
loop and scenario mode need a live connection to exercise, so this was throwaway): drove
`harness.py` over stdin against an in-process fake TCP server, running
connect/watch/status/heartbeat/putt/shot/raw/shotnum/status/quit in sequence. Confirmed:
async `connect()` needs a beat before the socket is actually up (expected — the REPL is
for a human who'll wait for the `[status] CONNECTED` line); ShotNumber sequencing and the
`shotnum` override both work correctly; transcript.log/.jsonl match the spec's format
exactly. Caught and fixed one real bug this way: `COMMAND_HELP` used em-dash characters
that came out as `�` in the Windows console — replaced with plain ASCII hyphens.

**Also resolved:** `SPEC.md`'s folder layout said `logs/` is "gitignored," but the
repo's actual `.gitignore` (present since the initial architecture-docs commit) has an
explicit note that `prototypes/*/logs/` is deliberately **not** ignored, since
transcripts are the evidence `docs/GSPRO_OPENCONNECT.md`/`docs/GSPRO_PUTTING.md` get
written from. Flagged the conflict; confirmed the `.gitignore` behavior is correct and
fixed `SPEC.md`'s comment to match.

**Not yet done:** tickets 5–6 (`scenarios/`, `README.md`).

---

## 2026-09-15 — P0-2 ticket 3: GSProCodec + rolling_spin_rpm

**What happened:** Added `rolling_spin_rpm(speed_mph) -> 200.0 * speed_mph` and
`GSProCodec` to `protocol.py`. `GSProCodec` is constructed with `device_id`, `units`,
and a `next_shot_number` callable — normally a `GSProDirect` instance's
`next_shot_number` bound method — so the REPL and any scenario building payloads
through the same codec share one ShotNumber counter instead of two drifting apart.
`build_putt(speed, hla, vla, spin=None)` defaults `BackSpin`/`TotalSpin` to
`rolling_spin_rpm(speed)` unless overridden (experiment 10). `build_shot(speed, hla,
vla, total_spin, spin_axis, back_spin=None, side_spin=None)` covers full-shot payloads
(experiment 7); when `back_spin`/`side_spin` aren't given, `BackSpin` defaults to
`total_spin` and `SideSpin` to `0.0`.

Backed by 7 new tests in `test_protocol.py`: rolling-spin formula, putt spin
defaulting vs. explicit override, putt payload shape/ShotDataOptions, ShotNumber drawn
from the injected callable across two builds, and full-shot BallData with and without
explicit back/side spin. 28/28 tests pass.

**Not yet done:** tickets 4–6 (`harness.py`, `scenarios/`, `README.md`).

---

## 2026-09-15 — Windows PC PATH fixed

Added the real Python 3.14.5 install directory
(`C:\Users\blake\AppData\Local\Python\pythoncore-3.14-64`) to the front of the User PATH
via `[Environment]::SetEnvironmentVariable`. Bare `python`/`py` on PATH were previously
resolving to a stale Microsoft Store app-execution-alias stub. New terminals will pick
this up automatically (already-running shells at the time of the change won't, since
Windows only applies PATH changes to newly spawned processes).

---

## 2026-09-15 — P0-2 ticket 2: ConnectionState + GSProDirect

**What happened:** Added `ConnectionState` (`DISABLED/CONNECTING/CONNECTED/RECONNECTING/
STOPPED`) and `GSProDirect` to `protocol.py`, ported from OpenFlight's `TcpSimClient`
with the renames from `SPEC.md`. Includes: background connect/reconnect thread with
backoff (`DEFAULT_BACKOFF = (1,2,4,8,16,30)`s), `_recv_loop` draining via
`find_json_end` with the 64KB resync guard, `send()` raising `ConnectionError` when not
connected, `send_heartbeat(ready, ball_detected=False)` with no automatic timer,
`ShotNumber` seeded from `int(time.time())` with `next_shot_number()`/
`override_shot_number()`, and a `drop()` method that forces a TCP RST (via
`SO_LINGER`) rather than a clean FIN, to simulate a dropped link for experiment 16 —
distinct from `disconnect()`'s orderly shutdown. Callbacks: `on_send`, `on_receive`,
`on_response`, `on_status` (wired up by `harness.py` in ticket 4).

Backed by 10 new tests in `test_protocol.py` against a small in-process fake TCP server
(no real GSPro needed): connect/disconnect lifecycle, heartbeat payload shape, shot
number seeding/override, send-while-disconnected raises, inbound frame callbacks,
status-transition callbacks, drop-then-reconnect, and CONNECTING-vs-RECONNECTING
(confirms a connection that never succeeded stays CONNECTING through retries, never
reports RECONNECTING). 21/21 tests pass, run 3x clean (the drop/reconnect and
connect-transition tests are timing-dependent, so re-ran to check for flakiness).

**Not yet done:** tickets 3–6 (`GSProCodec`, `harness.py`, `scenarios/`, `README.md`).

---

## 2026-09-15 — P0-2 ticket 1: wire format + framing

**What happened:** Implemented the first of six tickets breaking down `SPEC.md`:
`protocol.py`'s dataclasses (`BallData`, `ClubData`, `ShotDataOptions`, `ShotPayload`,
`GSProResponse`), `serialize_payload`/`parse_response`, and `find_json_end` — ported
from `openflight/gspro/messages.py` and `openflight/sim/transport.py`. Backed by
`test_protocol.py` (stdlib `unittest`): 11 tests covering serialization shape, response
parsing (well-formed, missing fields, malformed JSON), and framing edge cases (two
objects in one buffer, one object split across two reads, brace/escaped-quote inside a
string not affecting depth, no-complete-frame, >64KB with no complete frame). All 11
pass.

**Environment note (Windows PC):** the `python`/`py` commands on PATH resolve to a stale
Windows Store app-execution-alias stub, not a real interpreter — running them prints
"Python was not found; run without arguments to install from the Microsoft Store." A real
Python 3.14.5 *is* installed (via the PSF Python Install Manager) at
`C:\Users\blake\AppData\Local\Python\pythoncore-3.14-64\python.exe`, and its own `py.exe`
launcher (`C:\Program Files\WindowsApps\PythonSoftwareFoundation.PythonManager_.../py.exe`)
works correctly. Until PATH or the App execution alias setting is fixed, invoke that full
path directly (or `py.exe` from the PythonManager install dir) rather than bare `python`.
This will also affect running `capture.py` for P0-1.

**Not yet done:** tickets 2–6 (`ConnectionState`/`GSProDirect`, `GSProCodec`, `harness.py`,
`scenarios/`, `README.md`).

---

## 2026-09-15 — P0-2 planning: spec written, no code yet

**What happened:** Read the three authoritative docs, then ran a `/grilling` interview
(with `domain-modeling`) to settle P0-2's implementation-level decisions before writing
any code. Output: `prototypes/gspro-harness/SPEC.md`.

**Decisions locked in:**
- `protocol.py` mirrors OpenFlight's Codec/Transport split conceptually, but stays one
  file (matches the Harness Specs' named deliverable list) — no separate `transport.py`.
- Full `ConnectionState` + backoff/reconnect loop ported from OpenFlight's
  `TcpSimClient`, but **no automatic background heartbeat thread** — heartbeats are sent
  explicitly by the REPL/scenario so experiments controlling readiness timing aren't
  fighting a timer.
- Two vocabulary corrections vs. the OpenFlight reference code, to stay consistent with
  this project's own established terms (Readiness Review §5, §6 item 5): the transport
  class is named `GSProDirect` (not `TcpSimClient`), and the reconnect state is
  `ConnectionState.RECONNECTING` (not `RECONNECT_BACKOFF`).
- Scenarios are modules under `scenarios/` exposing `run(client) -> None` plus a
  module-level `QUESTION` string; `harness.py` owns transcript/log setup centrally.
- REPL is a hand-rolled loop with a shared `parse_kv()` helper, not `cmd.Cmd`.
- `test_protocol.py` uses stdlib `unittest`, not `pytest` — zero dependencies to run or
  test this harness.
- Each session's screenshots and human observations live in `logs/session_<ts>/`
  alongside the transcripts, in a `NOTES.md` pre-seeded with that experiment's question.

**Not yet done:** no code written. Next step is breaking the spec into tickets and
implementing them one at a time, each shown for confirmation before moving on.

**Nothing measured yet.**

**What was surprising:** the draft was about to name the transport class `GSProClient` —
a plausible-looking name that would have silently drifted from `SimulatorConnector` /
`GSProDirect`, the vocabulary the Readiness Review already settled on for the exact same
role. Caught by cross-checking against the existing docs rather than inventing fresh
names for a "throwaway" prototype — worth remembering that "this code won't ship" doesn't
mean its *names* don't matter, since `protocol.py` is explicitly meant to be read again at
port time.
