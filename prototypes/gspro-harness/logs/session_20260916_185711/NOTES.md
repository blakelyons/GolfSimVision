# Which way does the ball go for HLA=+2 vs HLA=-2? Confirms the sign convention.

**Tentative: positive HLA broke left, negative HLA looked straighter/slightly right.**
Both putts (`speed=5, vla=0, spin=0`) got `200 "Ball Data received"` on the green with
`PT` selected. Blake's read: `HLA=+2` broke left; `HLA=-2` "looked straighter, maybe a
little to the right."

**Confound, not a clean result:** there was a little slope on this green, so some of the
apparent break may be gravity/slope rather than the `HLA` value itself — the `-2` result
in particular ("straighter") is weaker evidence than a flat-green test would give. Best
current read is **positive HLA = left, negative HLA = right**, but this should be treated
as provisional, not the same confidence level as exp03's headline result. If the sign
convention ever matters for correctness-critical code (not just this Phase 0
characterization), re-run on a known-flat green before trusting it.
