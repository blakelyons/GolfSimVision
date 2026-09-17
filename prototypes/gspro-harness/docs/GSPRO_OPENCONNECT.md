# GSPro OpenConnect (OpenAPI) Protocol Facts

Evidence rollup from P0-2's 20 live-GSPro experiments (`../logs/session_*/NOTES.md`
have the full evidence trail; this file is the distilled conclusions). Phase 0
prototype findings, not a vendor spec — treat anything marked provisional/uncertain
below accordingly.

## Readiness indicator (exp 1-4, closed)

`LaunchMonitorIsReady` (sent in our heartbeats) does **not** drive GSPro's on-screen
readiness indicator, under every condition tested: no round, active round, and
alternating `true`/`false` live while mid-round with a ball in play. Confirmed three
separate times (`logs/session_20260916_140846`, `142854`, `162907`).

What actually correlates: GSPro sends an inbound `{"Code":202,"Message":"GSPro
ready","Player":null}` roughly 5-15s after each shot's `200` ack, once its flight/
animation settles. **Watch for inbound Code 202, not the `LaunchMonitorIsReady` field we
send, to know when GSPro is ready for the next shot.**

## ShotNumber rules (exp 13, confirmed)

GSPro accepts any `ShotNumber` that fits in a signed 32-bit int — non-monotonic, `0`,
and negative values all get `200 "Ball Data received"` — and rejects with `501 "Bad
format"` the moment it overflows int32 (`>= 2147483648`). No connection drop either way;
GSPro keeps responding normally to heartbeats between and after the rejections.
(`logs/session_20260916_191159`)

## Reconnect behavior (exp 16, confirmed)

After a forced TCP RST (not a clean close) and reconnect, GSPro recovers cleanly: a
fresh heartbeat gets the normal response set back (`201` Player Info x2, `202 GSPro
ready`), and a subsequent shot gets `200`. No evidence GSPro tracks or requires any
particular "expected next" ShotNumber across a reconnect — combined with exp13, it
appears to accept whatever valid-range ShotNumber the next request carries, drop or no
drop. (`logs/session_20260916_200833`)

## Ack correlation under rapid sends (exp 15, real production implication)

Sending 3 shots with ~0ms gaps between them (no pacing) got only **2 of 3 responses
back**: one `501 "Bad format"`, one `200 "Ball Data received"`, and the third send's
fate is simply unknown (no ack, no error). `GSProResponse` carries no ShotNumber or
other correlating field, so responses cannot be mapped to specific requests even when
all arrive. (`logs/session_20260916_191703`)

**Real implication for the production app:** don't fire shot payloads back-to-back with
no gap. Use a minimum inter-shot delay, or wait for the prior `200`/`202 GSPro ready`
before sending the next one.

## APIversion casing (exp 14, confirmed)

Both the spec-correct lowercase-v key (`"APIversion":"1"`) and the capital-V variant
`gspro-r10.exe` actually sends (`"APIVersion":"1"`) got `200 "Ball Data received"`.
GSPro is not case-sensitive on this key, at least for the values tested.
(`logs/session_20260916_191642`)

## Heartbeat absence (exp 18, confirmed within tested window)

After one registering heartbeat, 5 minutes of complete silence (no heartbeat, no shot
traffic) produced no timeout and no disconnect — `client.state` stayed `CONNECTED` the
entire time, confirmed at 30s checkpoints, and no inbound traffic arrived either. GSPro
does not appear to enforce any heartbeat-absence timeout, at least not within 5 minutes.
(`logs/session_20260916_200847`)

## Malformed input (exp 19, uncertain mapping — flagged, not resolved)

Sent a `BallData` object missing required fields (only `Speed` present), then ~1ms
later an outright malformed JSON literal. Neither dropped the connection — GSPro kept
responding normally to later scenarios afterward. But only 2 responses came back for the
2 sends (`501` then `200`), with the same near-zero-gap pacing issue as exp15, so which
response belongs to which request is **not reliably known**. Taking send order = response
order at face value would mean the missing-field payload got rejected (`501`) and the
malformed JSON got accepted (`200`) — a genuinely surprising result if true, but not
trustworthy without a deliberately-paced re-run (wait for each response before sending
the next). (`logs/session_20260916_191710`)

**Confirmed regardless of mapping:** neither a missing required field nor outright
malformed JSON drops the connection.

## No round active (exp 17, confirmed)

A shot sent with no round active (at GSPro's main menu) is silently ignored — no `200`,
no error code, nothing — even with a shutdown grace period to catch a late response.
GSPro accepts the shot at the TCP/JSON level (no error, no dropped connection) but does
nothing with it. (`logs/session_20260916_155926`)

## ContainsBallData precedence (exp 20, confirmed)

When `ShotDataOptions.ContainsBallData` is `false` but a full, populated `BallData`
object is present anyway, GSPro honors `ContainsBallData`, not the presence of the data:
no ball movement on screen, and no protocol response at all (`200`, `501`, nothing),
even after a 10s watch on a connection confirmed alive (heartbeat got normal `201`/`202`
replies first). GSPro appears to silently drop shots marked this way.
(`logs/session_20260916_195635`)

## Club change / Player object (exp 5, confirmed)

`Club` reliably reads the expected 2-letter code for every selection, including `"PT"`
for the putter, cycling the full bag. Every club change fires **two** `201`s about
0.2-0.3s apart, not one — dedupe on this in any real consumer. `DistanceToTarget` moves
as clubs change, consistent with an estimated carry/total distance for the selected club
from the current lie, not a fixed hole distance. (`logs/session_20260916_161202`)

## HLA sign convention (exp 11, provisional — not to be trusted blindly)

Best current read: **positive HLA broke left, negative HLA looked straighter/slightly
right.** But this test ran on a green with a little slope, so some of the apparent break
may be gravity/slope rather than the `HLA` value itself — the `-2` ("straighter") result
in particular is weaker evidence than a flat-green test would give. Treat this as
provisional, not the same confidence level as the readiness-indicator finding above. If
the sign convention ever matters for correctness-critical code, re-run on a known-flat
green before trusting it. (`logs/session_20260916_185711`)

## New/undocumented response codes found

Not in the harness spec's original known-code list (200/201/>=500), and confirmed
silently dropped by the `openflight` reference implementation's own `parse_inbound()`
too — genuinely undocumented territory, not something specific to this harness:

| Code | Message | When seen |
|---|---|---|
| 200 | "Ball Data received" | immediately after every accepted shot |
| 201 | "GSPro Player Information" | on club change (fires twice per change) and after most shots |
| 202 | "GSPro ready" | ~5-15s after each `200`, once GSPro is ready for the next shot — this is what the UI readiness light actually reflects |
| 203 | "GSPro round ended" | appeared twice, unexplained, didn't actually end anything — play continued normally afterward. Benign anomaly, not chased further. |
| 501 | "Bad format" | ShotNumber overflow (exp13), and at least one of exp15/19's rapid-fire cases |

## Harness bugs found and fixed along the way

- **No shutdown grace period (fixed).** `run_scenario()` originally disconnected
  immediately after a scenario's `run()` returned, racing any in-flight GSPro response —
  a sent putt could show up in `transcript.log` with no response at all purely because
  the socket closed first. Fixed with a 2s grace period before shutdown.
- **Bare TCP connect isn't enough to register a client (fixed).** GSPro's Connect window
  can stay "Not Connected", and it won't push `201`s on club changes, until the client
  sends at least one real message. A TCP-only connect with no traffic looks like nothing
  to GSPro. Fixed by adding `connect_and_announce()` (connect + one heartbeat) used by
  every scenario.

## Operational finding: connection churn requires a live GSPro reset

Across this session, most `--scenario`/`--scenarios` invocations that ran back-to-back
eventually hit a state where GSPro stopped responding to anything at all (not even
heartbeats) — traffic looked correct in `transcript.log`, but GSPro did nothing. The
only fix found was a live **Settings > Game > Reset GSPro Connect** inside GSPro itself;
no amount of delay-tuning in `connect_and_announce()` fixed it. This harness performs a
full connect/disconnect cycle on every single process invocation — dozens of times in a
session — and that per-invocation churn appears to be what degrades GSPro's OpenAPI
connection handling over time, not any particular scenario's payload or timing.

Chaining several scenarios over **one** continuous connection (`--scenarios name1,name2,...`,
added mid-session) reduced how often a reset was needed — once reset, up to 4 chained
scenarios ran cleanly with no further resets — but did not eliminate the need for an
initial reset once GSPro's connection state had already degraded from earlier separate
invocations.

**This should not carry forward to the production GolfSimVision app.** The app will
hold one persistent OpenConnect connection for an entire round, the way a real launch
monitor does — it will never hit this per-invocation connect/disconnect churn pattern in
the first place. This is specific to this one-scenario(or few)-per-process Phase-0 test
tool, not a GSPro limitation the shipped app needs to work around.
