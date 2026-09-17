# Does a putt with VLA=0 and TotalSpin=0 get accepted, and what does the ball do on screen?

**Yes.** Sent `speed=5, hla=0, vla=0, spin=0` via `connect_and_announce` + one heartbeat.
GSPro responded immediately with `200 "Ball Data received"` (no reset needed this run).
Ball rolled normally on screen -- a straight rolling putt with no launch arc, as expected
for `VLA=0`. No visible oddities from `TotalSpin=0` either.
