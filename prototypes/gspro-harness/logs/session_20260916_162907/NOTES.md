# Manual REPL session -- exp06 (DistanceToTarget) done via simulated shots, no physical launch monitor

Not a scripted scenario -- driven entirely by hand via `python harness.py`'s REPL
(`shot`/`putt`/`heartbeat` commands), per RUNBOOK.md's guidance for exp06 when no
physical launch monitor is connected. Ended up playing through three holes' worth of
shots, which also answered several other open questions along the way.

## exp06 answered: yes, DistanceToTarget updates per shot, correctly, in yards

Watched across three holes:
- Hole 1: tee shot -> `DistanceToTarget` 391.6, `Club` auto-selected `SW` (94.7 left) ->
  approach -> `LW` (28.3, then 9.1) -> chip -> next hole started.
- Hole 2: tee shot -> 501.9, `W3` (231.2) -> `LW` (18.8) -> **`PT` auto-selected at 7.8
  yards** (confirms Blake's live observation: GSPro selects the putter automatically
  once close enough to the pin) -> putt -> 3.9 -> next hole started.
- Hole 3 barely started (`I6`, 159.1) before the session ended.

Units are yards throughout (`Units: "Yards"` in our payload, matches). Distance clearly
decreases shot-to-shot as expected, and GSPro auto-advances to the next hole and
auto-selects a starting club once the previous hole is holed out.

## Headline-question refinement: what actually drives the "Ready" indicator

Re-opened this after Blake reported the indicator went solid green mid-round, then ran
the decisive test: `heartbeat ready=false` **while still mid-round, ball in play** --
indicator stayed green. This confirms (a third time, now under real-shot conditions
too) that `LaunchMonitorIsReady` is not what drives it.

**What actually correlates:** GSPro sends an inbound `{"Code":202,"Message":"GSPro
ready","Player":null}` after processing each shot (roughly 5-15s after the `200` ack,
once the ball's flight/animation settles) -- see every shot cycle in transcript.log. The
UI's "Ready" light is almost certainly GSPro announcing its own readiness for the next
shot via this code, not reading anything from our `ShotDataOptions`. This resolves why
the flag never mattered: it was never the right signal to watch in the first place.
**Conclusion for docs/GSPRO_OPENCONNECT.md: watch for inbound Code 202, not the
LaunchMonitorIsReady field we send, to know when GSPro is ready for the next shot.**

## New response codes discovered (not in the harness spec's original list)

| Code | Message | When seen |
|---|---|---|
| 200 | "Ball Data received" | immediately after every shot -- the spec's generic "200 = shot received" now has exact wording |
| 202 | "GSPro ready" | ~5-15s after each 200, once GSPro is ready for the next shot -- see above |
| 203 | "GSPro round ended" | appeared twice, unexpectedly, ~1 minute into hole 1 (16:31:28-30), sandwiched between repeated identical `201`s. **Did not actually end anything** -- play continued normally through two more holes afterward. Unexplained; flagging as a benign anomaly rather than a real round-end event. Worth a dedicated follow-up if it recurs. |

## Shot calibration notes (useful for future sessions, not for docs)

- Driver, speed=150 vla=12 spin=2500: ~249yd raw carry per GSPro's on-screen readout,
  ~300.4yd "game" carry -- GSPro applies roughly a 1.2x scaling factor from raw physics
  to credited game distance. Worth confirming this holds at other speeds before relying
  on it.
- Wedge, speed=60 vla=28 spin=7500: closed ~94.7yd remaining to ~28.3 yards left, i.e.
  in the neighborhood of a 65-70yd shot -- reasonable full-wedge numbers to reuse.
- Putt, speed=4.5 vla=0 (spin auto via rolling-spin estimate): closed a 23ft putt in two
  strokes (7.8 -> 3.9yd -- the ball didn't go all the way in on this particular putt,
  based on the remaining distance after the second stroke, though Blake reported the hole
  finished successfully overall, likely on a subsequent short tap-in not captured before
  the next hole's tee shot appeared).

## Separate finding: REPL commands print nothing on success

`shot`/`putt`/`heartbeat` gave no confirmation output when run, unlike `shotnum` which
prints its result. This made it hard to tell whether `shot speed=150...` had actually
done anything the first time it was tried. Worth fixing in harness.py.

## Cross-check against ../example-apps/openflight-.../src/openflight/{sim,gspro}/ (read-only reference)

For docs/GSPRO_OPENCONNECT.md's write-up:

- `sim/transport.py`'s `TcpSimClient` runs a **continuous background heartbeat thread**
  (default every 5s) whenever the connection is otherwise idle -- a real launch monitor
  connector never goes fully silent. This harness deliberately doesn't do that (P0-2's
  design decision #2, for controllable single-variable experiments), which is the root
  reason `connect_and_announce()` was needed: without it, we look nothing like a real
  device to GSPro.
- `gspro/codec.py`'s `on_connect_bytes()` returns `None` for GSPro -- the reference
  doesn't send anything special immediately on connect either; it just lets the periodic
  heartbeat loop catch up within ~5s. So this harness's fix (one heartbeat immediately
  after connect) is a faster, deterministic version of the same underlying idea, not a
  divergence from known-good behavior.
- `gspro/codec.py`'s `parse_inbound()` only recognizes codes `200`, `201`, and `>=500` --
  **`202` and `203` are silently dropped**, not handled at all. Confirms these two codes
  are genuinely undocumented territory even in this "clean" reference implementation,
  not something we're missing from a known spec.
- Minor difference, not necessarily worth adopting: the reference's heartbeats always
  hardcode `ShotNumber=0` rather than incrementing (`build_heartbeat(..., shot_number=0)`
  in `gspro/messages.py`), unlike this harness's `next_shot_number()` on every payload
  including heartbeats.
