# Manual REPL session -- decisive readiness-indicator test + connection-registration discovery

Not a scripted scenario -- run manually via `python harness.py` (REPL) after discovering
exp05's club changes produced no `201`s and GSPro's Connect window stayed "Not Connected"
despite our TCP socket connecting successfully.

## Finding 1: GSPro requires an actual message, not just a TCP connect, to register a client

Confirmed live: connected via `connect`, GSPro's Connect window stayed red/"Not
Connected". Sent `heartbeat ready=true` at 14:59:20 -- GSPro's window immediately showed
connected, and every club change from that point on produced two `201`s (one per some
internal double-fire on GSPro's side, consistently across every club changed). Before
that heartbeat, exp05 had produced zero `201`s despite an apparently-successful TCP
connect. **Root cause of exp01/exp05/exp06/exp17's earlier unreliable results** -- fixed
by adding `scenarios.connect_and_announce()` (one heartbeat right after connecting) to
all four. exp01 and exp17 need to be re-run with the fix.

## Finding 2: LaunchMonitorIsReady confirmed NOT to drive the readiness indicator (headline question, closed)

With the connection now properly registered (confirmed by live `201`s arriving), sent
heartbeats alternating `ready=true`/`ready=false` repeatedly over several minutes
(14:59:20 true, 15:01:42 false, 15:02:16 false, 15:03:07 true, 15:03:23 true, 15:03:28
false, 15:03:31 false, 15:03:35 true). Per Blake's live observation, the readiness
indicator stayed "Not Ready" throughout, never once responding to the flag in either
direction. Combined with exp02/exp03/exp04 (which also toggled the flag correctly but
whose connection-registration status was less certain before this fix), this closes the
headline question: **`LaunchMonitorIsReady` does not drive GSPro's readiness indicator,
under any condition tested** (no round, round active, indicator confirmed via a properly
GSPro-recognized connection).

## Side observation, not yet investigated

`Player.Handed` flipped from `"RH"` to `"LH"` and back between 14:59:54 and 14:59:55
without any input from us -- likely Blake toggling a setting in GSPro's own UI while
testing, not a protocol anomaly. Also: club changes consistently produced **two** `201`s
each (~0.25-0.3s apart), not one -- worth a note for whoever writes up
`docs/GSPRO_OPENCONNECT.md`'s club-change section (relevant to exp05's own question).
