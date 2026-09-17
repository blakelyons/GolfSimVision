# E6 Connect Protocol Facts (P0-4)

Evidence rollup from P0-4's live testing against the current Garmin Golf app with a real
R10, 2026-09-17 (`../logs/session_*/transcript.log` have the raw traffic; this file is
the distilled conclusions). Phase 0 prototype findings, not a vendor spec.

V1's `backend/monitors/r10.py` (ported here as `protocol.py`'s `E6Server`) was written
against an older version of the Garmin Golf app and had never been re-tested. All of the
below are things that changed, were never true, or were simply missing — found by
generating real protocol traffic (phone + R10, harness on the same WiFi) and reading the
resulting transcripts.

## Real shot message shape (confirmed live)

V1 assumed a single `SendShot` message carrying `BallData` directly. That is **not**
what the current app sends. The real sequence for one shot is:

```
Client -> {"Type":"SetBallData","BallData":{...real launch data...}}
Server -> {"Type":"ACK","SubType":"SetBallData","Details":"Success."}
Client -> {"Type":"SetClubData","ClubData":{...real club data...}}
Server -> {"Type":"ACK","SubType":"SetClubData","Details":"Success."}
Client -> {"Type":"SendShot"}                         <- empty, no BallData at all
Server -> {"Type":"ACK","SubType":"SendShot","Details":"Success."}
```

`SendShot` is just a "shot done" marker now, not a data carrier. `grep`ing multiple live
transcripts confirms `SendShot` never has a `BallData` field. (`logs/session_20260917_112832`,
`logs/session_20260917_114059`, `logs/session_20260917_122117`)

## Post-shot arm sequence (missing entirely in V1 — this was the big one)

ACK'ing `SendShot` is not enough. Without a further sequence of messages after it, the
app hangs indefinitely in a "waiting" state (a stuck yellow hourglass indicator, observed
live, regardless of how long you wait) and rejects the next shot with a client-side
"Shot Not Saved — Please start a game in E6 Connect, or try another shot if a game is
already in progress" popup, since from the app's perspective the previous shot's cycle
never closed.

Found by pulling up [`gsp-r10-adapter`](https://github.com/mholow/gsp-r10-adapter), a
known-working reference E6 Connect server (used to bridge the R10 to GSPro). Its
`R10Session.CompleteShot()` does this after `SendShot`'s ACK:

```
Server -> {"Type":"SimCommand","SubType":"Disarm"}
          (~0.5s pause)
Server -> {"Type":"SimCommand","SubType":"ShotComplete","Details":{
             "BallData": {...the shot's BallData...},
             "ClubData": {...the shot's ClubData...},
             "Apex":0,"BallInHole":false,"BallLocation":"Fairway",
             "CarryDeviationAngle":0,"TotalDeviationAngle":0,
             "CarryDeviationFeet":0,"TotalDeviationFeet":0,
             "CarryDistance":0,"TotalDistance":0,"DistanceToPin":0
          }}
          (~0.5s pause)
Server -> {"Type":"SimCommand","SubType":"Arm"}
```

Ported into `E6Server._complete_shot_sequence`, spawned right after `SendShot`'s ACK.
Confirmed live: after adding this, multiple shots in a row succeeded with no "Shot Not
Saved" popup and no stuck indicator.

**Known limitation, by design, not a bug:** the reference server hardcodes
`CarryDistance`/`TotalDistance`/`TotalDeviationFeet`/`TotalDeviationAngle` to `0` — it
does not compute real ball-flight physics, and neither does this harness. Confirmed live:
the Garmin Golf app correctly displays every field we *do* send real data for (backspin,
sidespin, face-to-path, launch direction, club path, club face, attack angle, launch
angle) but shows no data for total/carry distance or total deviation, exactly matching
the zeroed placeholders. Computing real carry/total distance from launch conditions is a
physics-engine problem for a later phase (GSPro itself does this for full shots via
ADR-0001's port-921 connection), not something this E6 ingress spike needs to solve.

## `SetClubData` must be ACK'd (bug found and fixed)

V1's handler only recognized `SetClubType` (club selection, e.g. `{"ClubType":"7Iron"}`)
— a different message from `SetClubData` (the per-shot club sensor data). `SetClubData`
fell through unhandled and was never ACK'd. Found live: a real shot's `SetClubData` went
unacknowledged, followed ~7s later by a `Disconnect`/reconnect loop that eventually got
stuck (this was before the disconnect watchdog existed too — see below).
(`logs/session_20260917_112832`)

## `Disconnect` handling (bug found and fixed)

`{"Type":"Disconnect"}` is sent by the app in two different situations that look
identical at the message level:

- **A same-socket soft restart.** `Disconnect` immediately followed (~10-20ms later) by
  a fresh `Handshake` on the *same* TCP connection. Observed live correlating with R10
  Bluetooth wake-up events and the app's own reconnect-popup UI churn. Harmless —
  our server answers every `Handshake`/`Challenge` instantly and correctly underneath,
  and shot capture keeps working despite the popup showing.
- **A real end-of-session disconnect.** `Disconnect` with no follow-up `Handshake` —
  the app truly ended the session.

V1 had no `Disconnect` handler at all, so the harness reported `CONNECTED` forever even
after the user ended the session in the app. Fixed with a grace-period watchdog
(`DISCONNECT_GRACE_SECONDS`, 1s in production): on `Disconnect`, wait out the grace
window, and only close the socket if nothing else arrived in that time. Confirmed live
twice: a real end-of-session `Disconnect` correctly flips the harness to `DISCONNECTED`
after the grace period, and mid-session soft-restart `Disconnect`+`Handshake` pairs are
correctly left alone (connection and shot capture both kept working through them).
(`logs/session_20260917_114059`, `logs/session_20260917_122117`)

## Duplicate ping threads on reconnect (bug found and fixed)

Each accepted connection spawned its own ping-loop thread, but nothing stopped a prior
connection's thread when a new one was accepted — every reconnect left another ping
loop running forever, all firing `SimCommand`/`Ping` at the single current client at
once. Fixed with a generation counter: every background thread (recv, ping, disconnect
watchdog) captures the generation it was born into and stops as soon as a newer
connection is accepted.

A related variant surfaced after the disconnect-watchdog fix: `_ping_loop` originally
only checked the generation counter, not whether the connection was still live — so
after a *real* disconnect closed the socket (no new connection accepted yet, generation
unchanged), the ping loop kept waking up every `PING_INTERVAL_SECONDS`, hit an `OSError`
trying to send on the dead socket, and re-fired `on_connection_change(False)` every
cycle. Observed live as the harness printing `[status] DISCONNECTED` every ~10s forever
after ending a session. Fixed by also checking `self._connected` in the ping loop's
condition.

## Empty `SendShot` double-firing a shot callback (bug found and fixed)

Once `SetBallData` was confirmed as the real shot-data carrier, `on_shot` was fired from
both `SetBallData` (real data) and `SendShot` (used to default to `{}` when absent) —
double-counting every real shot with a second, empty-dict "shot". Observed live as the
harness printing a `[shot #2] BallData = {}` immediately after every real shot. Fixed by
only firing `on_shot` from `SetBallData`; `SendShot` is ACK-only.
