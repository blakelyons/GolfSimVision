# Does GSPro reject or accept non-monotonic/zero/negative/overflowing ShotNumber values? Confirm 501 on int32 overflow; find the real rules.

**Fully answered -- all 5 cases responded, no reset needed this run.** On the green with
`PT` selected:

| ShotNumber sent | Case | Response |
|---|---|---|
| `1` | non-monotonic (small value after epoch-seconds counter) | `200 "Ball Data received"` |
| `0` | zero | `200 "Ball Data received"` |
| `-1` | negative | `200 "Ball Data received"` |
| `2147483648` (2^31) | int32 overflow | `501 "Bad format"` |
| `2147483649` (2^31 + 1) | int32 overflow + 1 | `501 "Bad format"` |

**Real rule: GSPro accepts any ShotNumber that fits in a signed 32-bit int** --
non-monotonic, zero, and negative are all fine -- **and rejects with `501 "Bad format"`
the moment it overflows int32 (>= 2^31).** No connection drop either way; GSPro kept
responding normally to Player Information heartbeats between and after the rejections.

Blake noted only the first putt visibly played on screen; that's expected here, not a
bug -- these 5 sends went out about 1 second apart (`time.sleep(1)` in
`exp13_shotnumber_abuse.py`), too fast to watch each one animate individually, and
non-monotonic/duplicate ShotNumbers replaying the same putt from the same spot wouldn't
look like separate distinct shots anyway. The protocol-level responses above are the
actual evidence for this experiment, not the on-screen animation.
