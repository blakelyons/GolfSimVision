# GSPro Protocol Truth Harness (P0-2)

A fake launch monitor: it talks to GSPro's OpenConnect socket (TCP 921) and answers the
20 questions in `docs/GolfSimVision-Phase0-Harness-Specs.md` §P0-2. Phase 0 throwaway
tooling — see `SPEC.md` for the full design and the vocabulary that carries forward to
the future C# `SimulatorConnector`/`GSProDirect`.

**Ships:** nothing. **Runs on:** the Windows PC, always — GSPro listens on
`127.0.0.1:921` and this harness must be on the same machine.

See `RUNBOOK.md` for the recommended order to actually run all 20 experiments in a
session, and how to roll the results up into `docs/GSPRO_OPENCONNECT.md` /
`docs/GSPRO_PUTTING.md` afterward. This file is the reference for what each one does.

## Prerequisites

- Python 3 (standard library only — `socket`, `json`, `dataclasses`, `threading`, `enum`,
  `time`, `argparse`, `unittest`). No `requirements.txt`, nothing to `pip install`.
- GSPro running on the same Windows PC, with **Connect > OpenConnect (E6 Direct)**
  active, before you run `connect` (REPL) or a scenario.
- For scenarios that ask you to screenshot GSPro's UI: have a screenshot tool ready and
  drop the images into that scenario's `logs/session_<ts>/` folder.

## Running (Windows PC)

Interactive REPL:

```
python harness.py
```

Optional flags, all default to GSPro's normal setup: `--host 127.0.0.1 --port 921
--device-id GolfSimVision --units Yards`.

One scenario, non-interactively (imports `scenarios.NAME`, calls `run(client)`, exits
when it returns):

```
python harness.py --scenario exp03_readiness_true
```

Either mode creates `logs/session_<ts>/` containing `transcript.log` (human-readable),
`transcript.jsonl` (machine-readable), and `NOTES.md` (pre-seeded with the scenario's
`QUESTION`, if run via `--scenario` — fill in the answer once you've observed GSPro).
Logs are committed as evidence, not gitignored.

## REPL command reference

| Command | Args | Behavior |
|---|---|---|
| `connect` | — | opens the connection, starts the reconnect-loop thread |
| `disconnect` | — | orderly close |
| `drop` | — | closes the raw socket without a clean shutdown, simulating a dropped link (experiment 16) |
| `heartbeat` | `ready=<bool> [detected=<bool>]` | sends a heartbeat with the given fields |
| `putt` | `speed=<mph> hla=<deg> vla=<deg> [spin=<rpm>]` | builds and sends a putt payload; `spin` defaults to the rolling-spin estimate (`200 * speed`) |
| `shot` | `speed=<mph> hla=<deg> vla=<deg> spin=<rpm> [spinaxis=<deg>]` | builds and sends a full-shot payload |
| `raw` | `<literal JSON>` | sends the text exactly as typed, bypassing the codec — for malformed-input and casing tests (14, 19, 20) |
| `shotnum` | `<int>` | forces the next auto-incremented `ShotNumber` (experiment 13) |
| `watch` | `<seconds>` | waits, logging inbound traffic only, sends nothing |
| `status` | — | prints `ConnectionState`, last `ShotNumber` sent, session uptime |
| `help` | — | lists these commands |
| `quit` / `exit` | — | flush + close transcripts, disconnect, exit |

Example REPL session:

```
> connect
[status] CONNECTED
> heartbeat ready=true
> putt speed=5 hla=0 vla=0
> status
state=CONNECTED last_shot_number=1758042831 uptime=12.3s
> quit
```

## Scenarios

Every `scenarios/exp##_*.py` exposes a module-level `QUESTION` and `run(client)`, per
`SPEC.md`'s scenario contract. `scenarios/__init__.py` provides two helpers scenario
files use to work with just the `client` they're handed: `make_codec(client)` (a
`GSProCodec` wired to that client's device id / units / shot numbering) and
`connect_and_wait(client, timeout=10.0)` (blocks until connected, since `connect()`
only starts a background thread and scenarios run mostly unattended).

| # | Scenario | Question |
|---|---|---|
| 1 | `exp01_baseline_chatter` | What does GSPro send unprompted, with no heartbeat or shot traffic, over 60s? |
| 2 | `exp02_readiness_false` | With `LaunchMonitorIsReady=false` sent every 5s for 30s, what does GSPro's readiness indicator show? |
| 3 | `exp03_readiness_true` | With `LaunchMonitorIsReady=true` sent every 5s for 30s, does GSPro's readiness indicator change? (The headline question — every heartbeat gspro-r10.exe sends carries `false`.) |
| 4 | `exp04_readiness_toggle` | Alternating `LaunchMonitorIsReady` every 10s for 2 minutes, does the indicator track live, or latch? |
| 5 | `exp05_club_change` | What Player object does each club produce in GSPro's 201 response? Does `Club` reliably read `PT` when the putter is selected? |
| 6 | `exp06_distance_to_target` | Does `DistanceToTarget` update per shot as a hole is played tee to green? What units, and what happens once on the green? |
| 7 | `exp07_full_shot` | Does a full-shot payload (driver-range numbers) play correctly in GSPro? What response code comes back? |
| 8 | `exp08_putt_vla_zero` | Does a putt with `VLA=0` and `TotalSpin=0` get accepted, and what does the ball do on screen? |
| 9 | `exp09_vla_sweep` | Which VLA value (0, 0.5, 1.0, 2.0) makes a putt look correct on screen? |
| 10 | `exp10_totalspin_sweep` | Does a realistic rolling-spin estimate (`200 * mph`) behave differently — and better — than `TotalSpin=0` for a putt? |
| 11 | `exp11_hla_sign` | Which way does the ball go for `HLA=+2` vs `HLA=-2`? Confirms the sign convention. |
| 12 | `exp12_speed_range` | At what speed (1, 3, 5, 8, 12 mph) does GSPro start to misbehave, at the low or high end? |
| 13 | `exp13_shotnumber_abuse` | Does GSPro reject or accept non-monotonic/zero/negative/overflowing `ShotNumber` values? Confirm 501 on int32 overflow; find the real rules. |
| 14 | `exp14_apiversion_casing` | Is `APIversion` casing checked? gspro-r10.exe sends the capital-V form `APIVersion` and works — is GSPro actually case-insensitive here? |
| 15 | `exp15_ack_correlation` | Sending 3 shots in rapid succession, how many 200 responses come back, and is there any way to correlate a response to a specific shot? |
| 16 | `exp16_reconnect` | After a dropped connection (RST, not a clean close) and reconnect, does GSPro recover? What `ShotNumber` does it expect next? |
| 17 | `exp17_no_round_active` | With no round active (at GSPro's menu), is a shot accepted, ignored, or does it error? |
| 18 | `exp18_heartbeat_absence` | With no heartbeat or shot traffic for 5 minutes, does GSPro time us out or close the connection? |
| 19 | `exp19_malformed_input` | What error code (if any) comes back for a payload missing a required `BallData` field, and for outright malformed JSON? Does GSPro drop the connection either way? |
| 20 | `exp20_contains_balldata_false` | When `ContainsBallData` is false but a full `BallData` object is present anyway, which does GSPro honor? |

Scenarios 1, 5, 6, 17 are passive/observational — they prompt you to act in GSPro (watch,
change clubs, play a hole, check you're at the menu) rather than sending a sweep
themselves. Several (`exp02`–`exp04`, `exp09`, `exp12`) pause with `input()` between steps
so you can screenshot or observe before continuing.

## Testing

Pure-logic tests, no GSPro or socket needed, run from `prototypes/gspro-harness/` on
either machine:

```
python -m unittest
```

Covers `protocol.py`'s framing/codec/connection-state logic (`test_protocol.py`) and
`harness.py`'s REPL parsing (`test_harness.py`), plus structural coverage confirming all
20 scenario modules satisfy the `QUESTION` + `run(client)` contract (`test_scenarios.py`).
None of this exercises actual GSPro behavior — that only happens by running scenarios (or
the REPL) against a live GSPro instance and recording the result in each session's
`NOTES.md`.

## Exit criteria

Unchanged from `docs/GolfSimVision-Phase0-Harness-Specs.md`: all 20 experiments run with
transcripts, experiment 3 answered with evidence, a verified putt payload written to
`docs/GSPRO_PUTTING.md`, HLA sign confirmed, ShotNumber rules confirmed,
`test_protocol.py` passing.
