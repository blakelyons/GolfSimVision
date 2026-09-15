# Phase 0 prototypes

Validation harnesses. **None of this code ships.** It exists to answer questions and
produce data; the settled pipeline is ported to C# in Phase 1 (ADR-0006).

| Harness | Purpose | Runs on |
|---|---|---|
| `corpus/` | Record, label and replay a putting corpus (P0-1) | capture on Windows; analysis anywhere |
| `gspro-harness/` | Fake launch monitor to settle GSPro's real behaviour (P0-2) | Windows |

Build specs: [`../docs/GolfSimVision-Phase0-Harness-Specs.md`](../docs/GolfSimVision-Phase0-Harness-Specs.md)

Keep a running `PROGRESS.md` here: what was built, how to run it, what was measured,
what was surprising.
