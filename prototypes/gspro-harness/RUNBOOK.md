# P0-2 Experiment Runbook

A session-level guide for actually running the 20 experiments in `README.md`'s table.
That table is the reference for *what each one does*; this doc is the order to run them
in, what state GSPro needs to be in for each phase, and what to do with the results
afterward. **Runs entirely on the Windows PC** — GSPro and the harness must be on the
same machine (port 921 is loopback-only).

## Before you start

- [ ] GSPro's licence is the **Open Connect / OpenAPI** type, not R10-bound (an R10-bound
      licence disables port 921 — convert at `gsprogolf.com/convert.html` if needed).
- [ ] GSPro is running, **Connect > OpenConnect (E6 Direct)** is active, and the Connect
      window shows "Waiting for connection."
- [ ] **`gspro-r10.exe` is not running.** Port 921 is single-client — it will hold the
      socket and the harness will fail to connect.
- [ ] A screenshot tool is ready (Win+Shift+S is fine) for experiments 3-4.
- [ ] `python -m unittest` passes in this folder (confirms the harness itself is sound
      before you spend time on live GSPro runs).

Run everything from `prototypes/gspro-harness/`:
```
python harness.py --scenario exp01_baseline_chatter
```
Each run creates `logs/session_<ts>/` with `transcript.log`, `transcript.jsonl`, and a
`NOTES.md` pre-seeded with that experiment's question — fill in the answer once you've
observed GSPro. Drop any screenshots into that same `logs/session_<ts>/` folder.

## Phase 1 — Menu state, no round needed (~15 min)

Do these first, while GSPro is at its main menu — no round to set up or tear down yet.

| # | Scenario | Note |
|---|---|---|
| 1 | `exp01_baseline_chatter` | connect, log 60s, no traffic sent |
| 17 | `exp17_no_round_active` | confirms behavior with no round — do this **before** starting a round below, not after |
| 2 | `exp02_readiness_false` | 30s |
| 3 | `exp03_readiness_true` | **the headline question — screenshot GSPro's readiness indicator before and after** |
| 4 | `exp04_readiness_toggle` | 2 min, pauses for you to watch |

## Phase 2 — Start a round, normal play (~20 min)

Start a round in GSPro. These experiments use it the way a player normally would.

| # | Scenario | Note |
|---|---|---|
| 5 | `exp05_club_change` | cycle the full bag, confirm `Club` reads `PT` for putter |
| 6 | `exp06_distance_to_target` | play a hole tee → green normally — this is the longest single experiment here |
| 7 | `exp07_full_shot` | driver-range numbers, confirms shots play at all |
| 8 | `exp08_putt_vla_zero` | baseline putt |
| 9 | `exp09_vla_sweep` | pauses between each VLA value — watch which looks right on screen |
| 10 | `exp10_totalspin_sweep` | 0 vs. the rolling-spin estimate |
| 11 | `exp11_hla_sign` | **confirms which way the ball goes for + vs. −** |
| 12 | `exp12_speed_range` | 1/3/5/8/12 mph |
| 15 | `exp15_ack_correlation` | 3 rapid shots — watch how many 200s come back |

## Phase 3 — Edge cases, still in the round (~10 min)

Deliberately abnormal input. Run these *after* Phase 2 so a connection drop here doesn't
cost you data you haven't collected yet.

| # | Scenario | Note |
|---|---|---|
| 13 | `exp13_shotnumber_abuse` | non-monotonic/zero/negative/overflow ShotNumber |
| 14 | `exp14_apiversion_casing` | sends `APIVersion` (capital V) like `gspro-r10.exe` does |
| 19 | `exp19_malformed_input` | missing field, then outright malformed JSON |
| 20 | `exp20_contains_balldata_false` | `ContainsBallData: false` with a real `BallData` anyway |

## Phase 4 — Connection resilience (~6 min, run last)

| # | Scenario | Note |
|---|---|---|
| 16 | `exp16_reconnect` | drops the raw socket (RST, not clean close), reconnects, checks GSPro's recovery and expected next ShotNumber |
| 18 | `exp18_heartbeat_absence` | 5 min of silence — kick this off and use the wait to fill in `NOTES.md` for earlier experiments instead of sitting idle |

## After all 20: roll the results up

Once every `logs/session_<ts>/NOTES.md` is filled in:

1. Write **`docs/GSPRO_OPENCONNECT.md`** — the protocol facts, pulled from the
   transcripts and notes: the readiness-indicator answer (exp 3-4), HLA sign convention
   (exp 11), ShotNumber rules including the 501-on-overflow confirmation (exp 13),
   ack correlation (exp 15), reconnect/expected-next-ShotNumber behavior (exp 16),
   `APIversion` casing tolerance (exp 14), heartbeat-absence timeout, if any (exp 18),
   malformed-input error codes (exp 19), no-round behavior (exp 17), and
   `ContainsBallData` precedence (exp 20).
2. Write **`docs/GSPRO_PUTTING.md`** — a verified working putt payload (from exp 7-10),
   which VLA/TotalSpin values actually look correct on screen, and the confirmed speed
   range GSPro handles cleanly (exp 12).
3. Check `README.md`'s exit criteria: all 20 run with transcripts ✓, experiment 3
   answered with evidence ✓, a verified putt payload written up ✓, HLA sign confirmed ✓,
   ShotNumber rules confirmed ✓, `test_protocol.py` passing ✓.

`logs/` is committed as evidence, not gitignored — commit the session folders along with
the two write-ups once they exist.
