# GSPro Putting — Verified Payload Shapes

Evidence rollup from P0-2 experiments 7-12 (`../logs/session_*/NOTES.md` have the full
evidence trail). Phase 0 prototype findings from a handful of manual observations on one
green — treat the "looks correct" calls below as weak signal, not verified fact.

## Verified working full-shot payload (exp 7)

Driver-range numbers played correctly on GSPro's course view (confirmed visually) and
got `200 "Ball Data received"` in ~0.2s:

```json
{
  "BallData": {
    "Speed": 150,
    "HLA": 0,
    "VLA": 12,
    "TotalSpin": 2500,
    "SpinAxis": 0
  }
}
```

(`logs/session_20260916_171044`)

## Verified working putt payload (exp 8)

```json
{
  "BallData": {
    "Speed": 5,
    "HLA": 0,
    "VLA": 0,
    "TotalSpin": 0
  }
}
```

Got `200 "Ball Data received"`, and the ball rolled normally on screen — a straight
rolling putt with no launch arc, as expected for `VLA=0`. No visible oddities from
`TotalSpin=0` either. (`logs/session_20260916_173249`)

## VLA sweep — which value looks correct (exp 9, inconclusive)

Swept `VLA` at 0, 0.5, 1.0, and 2.0 (speed=5, spin=0) on the green with `PT` selected.
All four got `200` and played. **Not a strong finding:** Blake's visual read was that the
four "might have looked slightly different, hard to tell" — no clear winner identified.
`DistanceToTarget` isn't a useful signal for comparing them either (see below).

Don't treat this as "any VLA value is fine" — it means this pass didn't distinguish them
well enough to pick one. A lower-speed putt or a side-by-side screenshot comparison
would likely be needed to actually tell them apart. (`logs/session_20260916_173545`)

## TotalSpin: zero vs. rolling-spin estimate (exp 10, weak positive)

Compared `TotalSpin=0` against the rolling-spin estimate (`200 * speed_mph` = 1000 at
speed=5) for an otherwise-identical putt. Both got `200`. Blake's visual read: the two
putts "looked slightly different" on screen — a real but subtle difference, not
dramatic. **Not confident enough to say the rolling-spin estimate looks better, only
that it's distinguishable from `TotalSpin=0`.** (`logs/session_20260916_184956`)

Formula used: `rolling_spin_rpm(speed_mph) = 200.0 * speed_mph` (`protocol.py`),
applied to both `BackSpin` and `TotalSpin`.

## Confirmed working speed range (exp 12)

All five tested speeds — 1, 3, 5, 8, 12 mph — got a clean `200 "Ball Data received"`
and looked visually normal ("appeared to behave"). **No misbehavior observed at either
the low end (1 mph) or high end (12 mph)** within this tested range.
(`logs/session_20260916_190815`)

## Caveats for anyone building on this

- All of the above was observed on **one green with one slope**, by one person, across
  a handful of putts each. None of it is a rigorous visual-quality study.
- `DistanceToTarget` replays from the same starting position for each new putt sent in a
  sweep (confirmed in both exp09 and exp10) — it only moves once per "hole" of actual
  progress, not once per payload sent. Don't use it to infer per-payload behavior
  differences.
- The HLA sign convention (which way `+`/`-` break) is documented separately in
  `GSPRO_OPENCONNECT.md` and is itself flagged provisional (confounded by green slope).
