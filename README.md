# GolfSimVision

A Windows desktop application that measures golf putts with a camera and sends them to
the **GSPro** golf simulator over its OpenConnect API — so that players whose launch
monitor cannot measure putting (such as the Garmin Approach R10) can putt without
leaving the simulator.

A second capability, **Shot Vision**, replays the impact window of a full shot from a
high-frame-rate buffer.

**Status:** Phase 0 — technical validation. No application code yet.

---

## Documentation

Read in this order:

| Document | What it is |
|---|---|
| [`docs/GolfSimVision-Project-Kickoff.md`](docs/GolfSimVision-Project-Kickoff.md) | Original requirements and project direction |
| [`docs/GolfSimVision-Project-Readiness-Review.md`](docs/GolfSimVision-Project-Readiness-Review.md) | Research findings, verified protocol facts, the five main risks |
| [`docs/GolfSimVision-Architecture-Decisions.md`](docs/GolfSimVision-Architecture-Decisions.md) | ADR-0001 … ADR-0008 — accepted, do not re-litigate |
| [`docs/GolfSimVision-Phase0-Harness-Specs.md`](docs/GolfSimVision-Phase0-Harness-Specs.md) | Build specs for the two Phase 0 harnesses |
| [`docs/GolfSimVision-Prototype-Appendix.md`](docs/GolfSimVision-Prototype-Appendix.md) | Early exploratory notes — **unverified; parts disproven** |

> The Prototype Appendix contains sample code built on a `HaishinKit` RTSP-server API
> that does not exist in any released version of that library. Treat everything in that
> file as a starting point for questions, never as an implementation reference.

---

## Key decisions

| | Decision | ADR |
|---|---|---|
| GSPro connection | GolfSimVision owns the single OpenConnect socket on TCP 921 | 0001 |
| R10 ingress | E6 Connect server on TCP 2483 for v1; native Bluetooth in v2 | 0002 |
| Camera model | Buffered burst, not continuous streaming. 120 fps measurement target | 0003 |
| Video source | USB webcam (reference implementation) and iPhone, behind one interface | 0004 |
| Calibration | One-time homography from a printed target. LiDAR for calibration/arming only | 0005 |
| Desktop stack | .NET 8 + WebView2, single signed executable | 0006 |
| Measurement stance | Precision over recall — drop a doubtful putt, and say why | 0007 |
| Distribution | Direct sale from golfsimvision.com | 0008 |

**Port 921 accepts one client at a time.** That single constraint is why GolfSimVision
must also own R10 ingress, and it shapes the whole architecture.

---

## Where things run

| Component | Machine | Why |
|---|---|---|
| `prototypes/corpus/capture.py` | **Windows** | DirectShow; the webcam |
| `prototypes/gspro-harness/` | **Windows** | GSPro listens on 127.0.0.1:921 |
| `label.py`, `replay.py`, `stats.py` | either | Offline analysis over a corpus |
| The application (Phase 1+) | **Windows** | OpenCvSharp, WebView2, Windows BLE |

Anything touching the camera, GSPro or the R10 is developed where it runs. Offline
analysis and docs are comfortable on either machine.

---

## Reference material

Not part of this repo. Lives alongside it in the workspace root — on the dev PC that is
`D:\SharedProjects\WebcamPutting\`, reachable from the MacBook as
`192.168.5.142 → /Volumes/SharedProjects/WebcamPutting`.

```
<workspace root>/
├── GolfSimVision/                      this repo
├── SimLinkPutting/                     V1 — see note below
├── example-apps/
│   ├── cam-putting-py/                 closest prior art; the benchmark baseline
│   ├── openflight-2f8920bc…/           OpenConnect codec + JSON framing reference
│   └── gspro-connector/                second independent OpenConnect implementation
└── Claude outputs/                     V1-era handoff notes
```

**V1 is `SimLinkPutting`** on the PC. It was called `SimPuttLink` on the MacBook — same
project, and older notes use both names. Read-only. Useful parts:

| Path | Why |
|---|---|
| `backend/monitors/r10.py` | Working E6 Connect handshake — ADR-0002 ports this |
| `backend/simulators/gspro.py` | Two-phase connection and reconnect logic worth keeping |
| `frontend/app/globals.css` | The design tokens to preserve |
| `CONTEXT.md`, `TESTING.md`, `docs/adr/` | What went wrong, in detail |

**V1's architecture is explicitly not the model for this project** — see ADR-0006 and
§7 of the Readiness Review. Its visual design is preserved; its engineering is not.

---

## Repository layout

```
docs/          architecture, decisions, verified protocol facts, research
prototypes/    Phase 0 validation harnesses (Python; none of this ships)
corpus/        putting corpus — gitignored, lives on the capture machine
```

`desktop/`, `ios/` and `shared/` arrive in Phase 1.
