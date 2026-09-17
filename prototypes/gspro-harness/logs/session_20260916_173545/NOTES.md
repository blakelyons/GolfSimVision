# Which VLA value (0, 0.5, 1.0, 2.0) makes a putt look correct on screen?

**Inconclusive on which looks "correct" — all four were accepted and played, differences
were hard to see.** Required a Settings > Game > Reset GSPro Connect after two dead
attempts (see `../session_20260916_173432/NOTES.md` and
`../session_20260916_173520/NOTES.md`) before this run accepted traffic.

All four putts (`speed=5, hla=0, spin=0`, VLA swept 0/0.5/1.0/2.0) got `200 "Ball Data
received"` and GSPro reported `DistanceToTarget: 5.1` after each one (only the first
moved it from `0.0`) — consistent with each putt replaying from the same starting
position rather than accumulating distance. Blake's visual read: the four VLA values
"might have looked slightly different, hard to tell" — no clear winner identified this
pass. Not treating this as a strong negative result; a lower-speed putt or a
side-by-side screenshot comparison would likely be needed to actually distinguish them,
but that's beyond this experiment's scope as run.
