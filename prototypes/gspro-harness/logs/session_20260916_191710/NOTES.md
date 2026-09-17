# What error code (if any) comes back for a payload missing a required BallData field, and for outright malformed JSON? Does GSPro drop the connection either way?

**No connection drop either way; got exactly one `501` and one `200`, mapping is
uncertain.** Sent the missing-field payload (`BallData:{"Speed":5.0}` only, no
SpinAxis/TotalSpin/HLA/VLA) then, ~1ms later, the outright malformed JSON literal
(`{"DeviceID":"GolfSimVision", not valid json}`) -- same near-zero-gap issue as
`../session_20260916_191703/NOTES.md` (exp15), since the harness's `input()` prompt
between them was apparently pressed before any response printed.

Two responses arrived, in this order: `501 "Bad format"`, then `200 "Ball Data
received"`. Taking send order = response order at face value: the **missing-field
payload got rejected (`501`)** and the **malformed-JSON literal got accepted (`200`)** --
which is a genuinely surprising result if true (a spec-shaped-but-incomplete object
rejected, while syntactically-broken JSON accepted). Given the pacing issue already seen
in exp15, this ordering is not fully trustworthy. Connection stayed up and kept
responding normally to later scenarios in the same batch either way, so "does it drop
the connection" is answered cleanly: **no, neither malformed input nor a missing field
drops the connection.**

Would need a slower, deliberately-paced re-run (wait for each response before sending the
next) to trust the specific 501-vs-200 mapping -- flagging as a real gap, not silently
resolved.
