# When ContainsBallData is false but a full BallData object is present anyway, which does GSPro honor?

**GSPro honors `ContainsBallData`, not the presence of `BallData`.** Sent via the REPL's
`raw` command (exp20's exact payload: real `BallData` with `Speed:5.0` etc., but
`ShotDataOptions.ContainsBallData:false`), on the green with `PT` selected, connection
confirmed alive (heartbeat got two `201 GSPro Player Information` replies first).

Result: **no ball movement on screen, and no protocol response at all** (`200`, `501`,
nothing) even after a 10s `watch`. Blake heard "a couple of dings" but didn't correlate
them to anything specific -- likely an unrelated GSPro UI sound, not evidence of the shot
being processed, given there was no ball movement and no ack.

**Answered: `ContainsBallData:false` wins over a populated `BallData` object.** GSPro
appears to silently drop shots marked this way -- no ack, no error, no visible effect.

Two earlier dead attempts preceded this one (both due to the ongoing connection-churn
issue, unrelated to the actual question):
- `../session_20260916_191717/NOTES.md` -- no response within the batch's 2s shutdown
  grace period, inconclusive.
- `../session_20260916_193717/NOTES.md` -- fully dead REPL session, not even the
  heartbeat got a response, required another GSPro reset.
