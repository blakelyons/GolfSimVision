# When ContainsBallData is false but a full BallData object is present anyway, which does GSPro honor?

**Inconclusive -- no response captured.** Sent a putt payload with real `BallData`
(speed=5) but `ShotDataOptions.ContainsBallData` forced to `false`. This was the last
scenario in the batch; no `200`/`501`/anything came back from GSPro within the 2s
shutdown grace period before the connection closed. Could mean GSPro silently ignores
shots with `ContainsBallData:false` (a real, useful answer if so), or could just be a
slow response that got cut off by shutdown -- can't tell which from this transcript
alone. Needs a re-run, ideally solo with a longer `watch` afterward (e.g. via the REPL:
`connect`, `heartbeat ready=true`, then a raw `putt`-equivalent with `ContainsBallData`
forced false, then `watch 10`) to rule out the timing explanation.

**Resolved:** see `../session_20260916_195635/NOTES.md` -- confirmed genuinely no
response (not a timing artifact), and no ball movement either.
