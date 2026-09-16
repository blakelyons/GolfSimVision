# P0-2 — GSPro Protocol Truth Harness — Implementation Spec

**Status:** Ready to implement. Companion to `docs/GolfSimVision-Phase0-Harness-Specs.md` §P0-2,
which remains authoritative for *what* the 20 experiments are and *why*. This document
settles *how* the harness itself is built — the output of a `/grilling` interview on
2026-09-15, cross-checked against `example-apps/openflight-*/src/openflight/sim/transport.py`
and `src/openflight/gspro/{codec,messages}.py`.

**Runs on:** the Windows PC. GSPro listens on `127.0.0.1:921`; this harness must run on the
same machine.

**Ships:** nothing. Per ADR-0006, `protocol.py` is the one piece "written with porting in
mind" — its shape is a direct rehearsal of the future C# `SimulatorConnector` /
`GSProDirect` (Readiness Review §5), so it's written carefully even though the file itself
is discarded.

---

## Vocabulary (must match the rest of the project)

| Term | Meaning | Source |
|---|---|---|
| `SimulatorConnector` | The pluggable-interface *role* — something that owns a simulator connection | Readiness Review §5 |
| `GSProDirect` | The concrete implementation of that role for GSPro's OpenConnect socket (TCP 921) | Readiness Review §5 |
| `Codec` | Stateless wire-format layer: builds outbound payloads, parses inbound ones | Readiness Review §6 item 1 (OpenFlight) |
| `CONNECTING` / `RECONNECTING` | Distinct connection states — "wrong port" vs. "worked once, then dropped" | Readiness Review §6 item 5 |

Two deliberate renames from the OpenFlight reference code, so the harness uses the
project's own words instead of importing another codebase's internal spelling:

- OpenFlight's `TcpSimClient` → **`GSProDirect`** here (it *is* the prototype of that class).
- OpenFlight's `ConnectionState.RECONNECT_BACKOFF` → **`ConnectionState.RECONNECTING`** here.

## Folder layout

```
prototypes/gspro-harness/
├── SPEC.md                 # this file
├── README.md               # prerequisites + how to run (Windows only)
├── protocol.py              # dataclasses, find_json_end, GSProCodec, ConnectionState, GSProDirect
├── test_protocol.py          # stdlib unittest — framing edge cases
├── harness.py                # REPL + scenario dispatch + logging setup
├── scenarios/
│   ├── __init__.py
│   ├── exp01_baseline_chatter.py
│   ├── exp02_readiness_false.py
│   ├── exp03_readiness_true.py
│   ├── ... one file per experiment in the Harness Specs' 20-row table
│   └── exp20_contains_balldata_false.py
└── logs/                      # created at runtime; committed as evidence, not gitignored
    └── session_<ts>/
        ├── transcript.log     # human-readable, V1's format
        ├── transcript.jsonl   # machine-readable
        ├── NOTES.md           # pre-seeded with the experiment's question; you fill in the answer
        └── *.png               # screenshots you drop in, named freely
```

No `requirements.txt` — everything is Python 3 standard library (`socket`, `json`,
`dataclasses`, `threading`, `enum`, `time`, `argparse`, `unittest`). Nothing to `pip
install` before you can run or test this.

---

## `protocol.py`

One file, three parts, matching the file the Harness Specs doc actually names (no
separate `transport.py` — see rationale in the naming table above; this is a ~few-hundred
line file, splitting later is cheap if it grows).

### 1. Wire dataclasses (ports `openflight/gspro/messages.py` almost verbatim)

`BallData`, `ClubData`, `ShotDataOptions`, `ShotPayload`, `GSProResponse` — same fields as
the spec (Readiness Review §3.2): `APIversion` is the **string** `"1"`; `BallData` requires
`Speed, SpinAxis, TotalSpin, HLA, VLA`; `ShotDataOptions` requires
`ContainsBallData, ContainsClubData`, with `LaunchMonitorIsReady`,
`LaunchMonitorBallDetected` (correct spelling), `IsHeartBeat` optional but always sent
explicitly here for clarity.

`serialize_payload()` → `json.dumps(..., separators=(",", ":"))`, UTF-8, no trailing
newline. `parse_response()` → raises `ValueError` on malformed JSON (caller decides
whether to log-and-continue or fail the scenario).

### 2. Framing — `find_json_end(buf: bytes) -> Optional[int]`

Ported as-is from `openflight/sim/transport.py`: balanced top-level braces, string- and
escape-aware, returns the index past the first complete object or `None`. Backed by
`test_protocol.py` (stdlib `unittest`) covering, at minimum:

- two complete objects in one buffer → both parsed
- one object split across two `extend()` calls → parsed once complete
- a `{` inside a string value → does not affect depth
- buffer > 64 KB with no complete frame → caller's resync path is exercised

### 3. Connection — `ConnectionState` + `GSProDirect`

```python
class ConnectionState(Enum):
    DISABLED = auto()
    CONNECTING = auto()
    CONNECTED = auto()
    RECONNECTING = auto()   # was once connected, dropped, retrying
    STOPPED = auto()
```

`GSProDirect` ports `TcpSimClient`'s connection lifecycle and reconnect/backoff loop
(`DEFAULT_BACKOFF = (1, 2, 4, 8, 16, 30)` seconds), its `_recv_loop` draining every
complete frame per `find_json_end`, its 64 KB resync guard, and its
`ConnectionError`-on-send-while-disconnected behavior (readiness review §6 item 4).

**Deliberately not ported:** the automatic heartbeat thread. `GSProDirect` exposes
`send_heartbeat(ready: bool, ball_detected: bool = False) -> None` as a plain method with
no background timer — the REPL or a scenario calls it exactly when the experiment wants
a heartbeat sent, with exactly the field values it wants to test (experiments 2, 3, 4, 18
all depend on precise control over heartbeat timing/content that an autonomous thread
would fight with).

`ShotNumber` seeded once per process from `int(time.time())` (epoch seconds — never
milliseconds, which overflow the signed 32-bit field and produce GSPro's `501`).
Auto-increments on every `send_shot()` / `send_putt()` call; the REPL's `shotnum <int>`
command can force the next value for experiment 13 (non-monotonic / overflow abuse).

`GSProCodec` wraps payload construction: `build_putt(speed, hla, vla, spin=None)` (spin
defaults to the rolling-spin estimate below, unless overridden — needed for experiment
10) and `build_shot(speed, hla, vla, total_spin, spin_axis, ...)` for full-shot tests
(experiment 7). Both return a `ShotPayload`; `GSProDirect.send(payload)` serializes and
sends, logging outbound in both the `.log` and `.jsonl` transcripts with `perf_counter`
timestamps.

**Rolling-spin helper** (experiment 10, Harness Specs §4):
```python
def rolling_spin_rpm(speed_mph: float) -> float:
    return 200.0 * speed_mph
```

---

## `harness.py`

CLI: `python harness.py [--host 127.0.0.1] [--port 921] [--device-id GolfSimVision] [--scenario NAME]`.

**No `--scenario`** → interactive REPL. **With `--scenario NAME`** → imports
`scenarios.NAME`, calls its `run(client)`, and exits when `run` returns.

Owns, once, for either mode:

- creating `logs/session_<ts>/`
- opening `transcript.log` (human-readable, V1's format: `HH:MM:SS.mmm  GSPro >> {...}`)
  and `transcript.jsonl` (one JSON object per line, both directions)
- constructing the one `GSProDirect` instance and wiring its send/receive callbacks to
  both transcript writers
- if `--scenario` was given: pre-writing `NOTES.md` with that scenario module's
  `QUESTION` string (a module-level constant each scenario file defines) so the file that
  lands in the session directory already has the question to answer, not a blank page

### REPL

Hand-rolled `while True: input("> ")` loop (not `cmd.Cmd`) plus one shared helper:

```python
def parse_kv(args: str) -> dict[str, str]:
    """'speed=5.0 hla=-1.5 vla=0' -> {'speed': '5.0', 'hla': '-1.5', 'vla': '0'}"""
```

used by every command that takes `key=value` pairs. Commands (dispatch via a
`{"connect": cmd_connect, ...}` dict keyed on the first token):

| Command | Args | Behavior |
|---|---|---|
| `connect` | — | opens the connection, starts the reconnect-loop thread |
| `disconnect` | — | orderly close |
| `drop` | — | closes the raw socket without a clean shutdown, to simulate a dropped link (experiment 16) |
| `heartbeat` | `ready=<bool> [detected=<bool>]` | calls `send_heartbeat` |
| `putt` | `speed=<mph> hla=<deg> vla=<deg> [spin=<rpm>]` | `GSProCodec.build_putt` + send |
| `shot` | `speed=<mph> hla=<deg> vla=<deg> spin=<rpm> [spinaxis=<deg>]` | full-shot payload + send |
| `raw` | `<literal JSON>` | sends the bytes exactly as typed — for malformed-input and casing tests (14, 19, 20) |
| `shotnum` | `<int>` | overrides the next auto-incremented ShotNumber |
| `watch` | `<seconds>` | logs inbound traffic only, sends nothing |
| `status` | — | prints `ConnectionState`, last `ShotNumber` sent, session uptime |
| `help` | — | lists the above |
| `quit` / `exit` | — | flush + close transcripts, disconnect, exit |

### Scenario contract

```python
# scenarios/exp03_readiness_true.py
QUESTION = "Does GSPro's readiness indicator change when fed LaunchMonitorIsReady=true?"

def run(client: "protocol.GSProDirect") -> None:
    client.connect()
    ...
    input("Screenshot the GSPro Connect window now, then press Enter to continue... ")
    ...
```

`harness.py` imports the module, reads `QUESTION` before calling `run`, so the
pre-seeded `NOTES.md` and the scenario logic can't drift out of sync. Scenarios that need
a human pause (screenshot, visual judgment call) use a plain `input()` — no special
machinery beyond that.

---

## Logging

Every byte both directions, `perf_counter`-timestamped, exactly as the Harness Specs
show:

```
14:06:00.688  GSPro >> {"Code":201,...}
14:06:01.855  GSPro << {"DeviceID":"GolfSimVision",...}
```

plus the parallel `.jsonl`. These transcripts, together with your `NOTES.md` answers and
screenshots, are what `docs/GSPRO_OPENCONNECT.md` and `docs/GSPRO_PUTTING.md` get written
from afterward — nothing here is discarded until those docs exist.

---

## Testing

`test_protocol.py`, stdlib `unittest`, run via `python -m unittest` (Windows or Mac — no
GSPro or socket needed, pure framing logic). No `pytest` dependency.

## Exit criteria

Unchanged from the Harness Specs doc — all 20 experiments run with transcripts,
experiment 3 answered with evidence, a verified putt payload written to
`docs/GSPRO_PUTTING.md`, HLA sign confirmed, ShotNumber rules confirmed,
`test_protocol.py` passing.
