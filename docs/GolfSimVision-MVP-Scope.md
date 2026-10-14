# GolfSimVision — MVP Scope (Phase 1)

**Date:** 2026-10-14
**Status:** Proposed — build order agreed with Blake, not yet started.
**Companions:** `GolfSimVision-Architecture-Decisions.md` (ADRs this scope implements),
`GolfSimVision-Phase0-Harness-Specs.md` (where the de-risked facts below came from).

---

## 1. What "MVP" means here

Per the Architecture Decisions doc's own framing: **"usable product" = GSPro connector +
R10 ingress + putting, all three.** Not two of three — GolfSimVision owning the single
921 socket (ADR-0001) means `gspro-r10.exe` can no longer sit in the loop for full shots,
so R10 ingress isn't a later phase, it's required for v1 to be usable for a round of golf.

This doc exists because Phase 0 has run long enough, and resolved enough of the real
unknowns, that further Python-prototype iteration has diminishing return. The call now is
to fast-track into the actual C#/WebView2 app and treat remaining tuning (detector
accuracy, calibration error bounds, camera comparison) as a parallel test/fix/refine
track once there's something real to test against — not a gate before building starts.

## 2. What Phase 0 already de-risked (reusable, not theoretical)

| Area | Verified fact | Source |
|---|---|---|
| GSPro protocol | Full OpenConnect wire format, putt payload, response codes, reconnect behavior | `GSPRO_OPENCONNECT.md`, `GSPRO_PUTTING.md` (P0-2, 20/20 experiments) |
| R10 ingress | E6 Connect handshake works against the *current* Garmin Golf app | P0-4 spike, live-verified |
| Camera backend | `CAP_MSMF` gets genuine ~60fps on the Brio; `CAP_DSHOW` caps at ~30fps regardless of request | `CAMERA_CAPABILITIES.md` (P0-1) |
| A live, working pipeline shape | `prototypes/webcam-putting/`: webcam capture → YOLO detect → V1's zone-state-machine → `GSProDirect` → GSPro, already wired end-to-end | Spike, Sept 2026 |
| Corpus + scoring harness | 85 labelled clips; baseline detector now scores non-zero (4 TP/19 FN/10 FP/18 double-fires) after today's fix | P0-1 |

`webcam-putting/`'s YOLO-based pipeline, not the `cam-putting-py` HSV baseline, is the
right MVP starting point — the baseline detector's only job was Phase-0 scoring/truth, per
its own harness spec; it was never meant to ship.

## 3. What's still genuinely open, and the call being made on each

- **P0-3 (calibration) — the ADR's own "#1 technical risk" — is untouched.** The MVP
  ships a one-time homography calibration *feature* (ADR-0005: ball-radius scale alone is
  explicitly not acceptable), but **its measured accuracy is not yet validated** and is
  carried forward as an open risk, not resolved before building starts.
- **P0-5 (camera/LED comparison)** and **P0-6 (iPhone track)** are deferred in full. ADR-0004
  names the webcam the reference implementation; the MVP is **webcam-only**. iPhone stays a
  real v2 destination, not dropped.
- **LiDAR arming** rides with P0-6 (iPhone-only hardware) — deferred with it.
- **Detector tuning** (double-fires, speed accuracy, false-positive rate) is explicitly
  post-MVP, now that the replay harness is proven able to score it (today's baseline fix).
- **Native BLE R10 / Rapsodo MLM2PRO ingress** stay v2 items per ADR-0002, unchanged by
  this scope. Worth a standing note: the `MLM2PRO-GSPro-Connector` repo found 2026-10-14
  has a complete, working `r10_device.py` + `.proto` BLE implementation — new evidence
  against ADR-0002's "no open-source BLE reference exists" premise — but it's GPL-3.0, so
  only the protocol *facts* (UUIDs, message schema, handshake order) are usable, never the
  code, given ADR-0008's closed-source direct-sale model. Revisit at v2 planning, not now.
- **Code signing, licensing, self-update** stay pre-paid-release items per the ADR doc's
  own "Open items" table — not pre-MVP blockers.

## 4. Build order

Six milestones. "Needs camera" flags what actually requires the webcam to be physically
set up — Milestone 1 doesn't, which matters right now.

### M1 — Shell + GSPro connector *(no camera needed)*
- .NET 8 + WebView2 skeleton, one process (ADR-0006 non-negotiables: typed messages on
  every interface, settings persisted outside any watched/reloaded tree, no module-level
  mutable globals, overlays drawn in exactly one place).
- Port `protocol.py`'s `GSProDirect`/`GSProCodec` to C# against the already-verified wire
  format — no new protocol discovery needed.
- A manual test-shot button in the UI to confirm shots land in GSPro and the readiness
  indicator behaves, without any camera or R10 in the loop.

### M2 — Webcam capture + preview *(needs camera)*
- OpenCvSharp, `CAP_MSMF` (not `CAP_DSHOW` — confirmed finding).
- The three-state buffered-burst model from ADR-0003 (full-shot/putting/stroke-detected),
  120fps measurement target, live preview as a separate lower-rate stream.

### M3 — Detection + zone/gateway UI *(needs camera)*
- Port the YOLO detector to ONNX Runtime (ADR-0006: "any ML via ONNX Runtime").
- Port `zone_state_machine.py` / `putt_calculator.py` from `webcam-putting/` largely
  unmodified — already a from-V1 port, proven live.
- Build the draggable start-zone/gateway box UI — V1's `CameraView.tsx` pattern
  (`dragging: 'zone' | 'gateway'`) is a direct reference; the backend concept
  (`detect_in_roi`, normalized coords) already exists in `zone_state_machine.py`.
- Wire precision-over-recall (ADR-0007): every rejection surfaces a visible,
  human-readable reason in the UI, not a log line — cheap to build in from day one, and
  it's what prevents V1's worst failure mode (bad numbers silently sent to GSPro).

### M4 — Calibration (minimal) *(needs camera)*
- One-time homography from a printed target or a known-distance manual entry.
- Explicitly logged as **unverified accuracy** pending the real P0-3 bench — ship the
  mechanism, not a validated error bound.

### M5 — R10 ingress *(needs phone + R10, not camera)*
- Port V1's `backend/monitors/r10.py` E6 Connect handshake to C#, against the
  already-live-verified protocol (P0-4).
- Fix V1's worst field failure by design: auto-detect and display the LAN IP in-app, and
  install the firewall rule from the installer rather than asking the user to run `netsh`.

### M6 — Integration pass *(needs everything)*
- A full round: webcam putts and R10 full shots in the same session. This is the actual
  "usable for golf" bar the architecture doc sets, not a feature checklist.

## 5. Explicitly out of scope for this MVP

P0-3's full accuracy bench, P0-5, P0-6, LiDAR arming, detector tuning beyond "it works,"
native BLE, multi-LM ingress, code signing/licensing/self-update. All real, all tracked,
none blocking the build.
