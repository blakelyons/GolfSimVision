# Does a realistic rolling-spin estimate (200 x mph) behave differently -- and better -- than TotalSpin=0 for a putt?

**Yes, slightly.** Run on the green with `PT` selected (confirmed via `Club:"PT"` in the
inbound Player Information). Both putts (`speed=5, hla=0, vla=0`) got `200 "Ball Data
received"`; `DistanceToTarget` went `0.0` -> `6.0` after the `TotalSpin=0` putt, then held
at `6.0` after the `TotalSpin=1000` (rolling-estimate) putt -- same "each putt replays
from the same start position" pattern seen in exp09, so distance-to-target isn't a useful
signal for comparing spin values.

Visually, Blake reported the two putts "looked slightly different" on screen -- a real
but subtle difference, not dramatic. Not confident enough from a single side-by-side to
say definitively that the rolling-spin estimate looks *better*, only that it's
distinguishable from `TotalSpin=0`.

**Two dead runs preceded this one**, both discarded:
- `../session_20260916_184526/NOTES.md` -- accidentally run while still on the tee box
  (`Club:"DR"`, `DistanceToTarget:391.6`), not a valid putt test.
- `../session_20260916_185033/NOTES.md` -- run immediately after this one, on the same
  green, zero GSPro response (see below).
