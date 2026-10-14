# Domain Docs

How the engineering skills should consume this repo's domain documentation when exploring the codebase.

## Before exploring, read these

- **`CONTEXT.md`** at the repo root, if it exists (none yet — proceed silently; `/domain-modeling` creates it lazily when terms or decisions actually get resolved).
- **`docs/GolfSimVision-Architecture-Decisions.md`** — this repo's consolidated ADR doc (ADR-0001…0008), read in full for the area you're touching. Not a `docs/adr/` directory — one file, not one-per-decision.
- **`docs/GolfSimVision-MVP-Scope.md`** — current Phase 1 build order and what's explicitly deferred.
- **`docs/GolfSimVision-Phase0-Harness-Specs.md`** — Phase 0 validation findings referenced by the ADRs.

If `CONTEXT.md` or any other file listed here doesn't exist, **proceed silently**. Don't flag its absence; don't suggest creating it upfront. The `/domain-modeling` skill (reached via `/grill-with-docs` and `/improve-codebase-architecture`) creates `CONTEXT.md` lazily when terms or decisions actually get resolved.

## File structure (this repo)

```
/
├── CLAUDE.md
├── docs/
│   ├── GolfSimVision-Architecture-Decisions.md   ← all ADRs, one file
│   ├── GolfSimVision-MVP-Scope.md
│   ├── GolfSimVision-Phase0-Harness-Specs.md
│   ├── GolfSimVision-Project-Kickoff.md
│   ├── GolfSimVision-Project-Readiness-Review.md
│   ├── CAMERA_CAPABILITIES.md
│   └── agents/                                   ← this skill's own output
├── prototypes/        Phase 0 harnesses (Python; none of it ships)
└── (desktop/, ios/, shared/ arrive in Phase 1)
```

If a future `CONTEXT-MAP.md` appears at the root, that signals a shift to multi-context (per-subsystem `CONTEXT.md`/ADRs) — not the case today.

## Use the glossary's vocabulary

When your output names a domain concept (in an issue title, a refactor proposal, a hypothesis, a test name), use the term as defined in `CONTEXT.md`. Don't drift to synonyms the glossary explicitly avoids.

If the concept you need isn't in the glossary yet, that's a signal — either you're inventing language the project doesn't use (reconsider) or there's a real gap (note it for `/domain-modeling`).

## Flag ADR conflicts

If your output contradicts an existing ADR, surface it explicitly rather than silently overriding:

> _Contradicts ADR-0007 (precision over recall) — but worth reopening because…_
