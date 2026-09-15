# GolfSimVision — Project Readiness Review

**Date:** 2026-09-15
**Status:** Pre-implementation. No code written. This document exists to be argued with.

Sources read for this review. Paths are relative to the workspace root that holds both
this repo and the reference material — `D:\SharedProjects\WebcamPutting\` on the dev PC.
*(The V1 project folder is named `SimLinkPutting` there; earlier notes may call it
`SimPuttLink`, which was its name on the MacBook. Same project.)*

| Source | Location |
|---|---|
| Kickoff, Prototype Appendix, Co-Work Prompt | `GolfSimVision/docs/` |
| **V1 (SimLinkPutting)** — full codebase, docs, ADRs, session logs | `SimLinkPutting/` |
| **OpenFlight** — full source, docs, archived plans | `example-apps/openflight-2f8920bc…` |
| **cam-putting-py** (alleexx) — closest prior art, with git history | `example-apps/cam-putting-py` |
| **gspro-connector** (kenjdavidson, Java) — second independent OpenConnect impl | `example-apps/gspro-connector` |
| **gspro-r10.exe v2.1.1** (.NET) — the R10 connector V1 was built around | `SimLinkPutting/launch_monitor_connectors/garmin_r10` |
| **GSPro Open Connect V1 spec** | gsprogolf.com/GSProConnectV1.html (fetched) |
| Apple AVFoundation / VideoToolbox / Network docs, HaishinKit source, OpenCV source, FFmpeg docs | primary sources |

---

## 1. What I understand GolfSimVision to be

A Windows desktop application that makes an iPhone act as a measurement camera for a golf simulator, so that a Garmin R10 + GSPro user can **putt** — which the R10 cannot measure — without leaving the simulator or switching applications.

Two capabilities, in priority order:

1. **Webcam Putting (the MVP).** Camera watches a fixed putting area. It detects a stationary ball, signals Ready, detects the stroke, tracks the ball, computes **ball speed and horizontal launch direction**, and submits that to GSPro as a shot.
2. **Shot Vision Feedback (later).** A rolling high-FPS buffer that, on a full-shot trigger, replays the impact window so the golfer can see clubhead and strike.

The phone is a sensor. The PC is the brain: ingest, computer vision, state, simulator integration, UI. LiDAR is an experiment, not a dependency. Nothing is to be assumed about hardware or API behaviour without measurement.

The governing question you set — *what is the best architecture if we built this correctly from scratch today* — is the right one, and V1 gives us an unusually good answer key, because V1's failures are documented in your own words in `CONTEXT.md`, `TESTING.md`, `docs/adr/`, and the session logs.

---

## 2. Component map and data flow

```
┌──────────────────── iPhone (foreground, screen on) ────────────────────┐
│  AVCaptureDevice  → format negotiation, locked manual exposure         │
│  AVCaptureVideoDataOutput → Y-plane only (zero-copy grayscale)         │
│  Rolling frame ring buffer (RAM)                                       │
│  Cheap on-device motion trigger (ROI frame-diff / vImage)              │
│  Transport: NWListener (TCP) — reachable over Wi-Fi *or* USB via iproxy │
│  Separate low-rate preview stream for alignment                        │
└───────────────────────────────┬────────────────────────────────────────┘
                                │ framed binary protocol (shared/protocol)
┌───────────────────────────────▼────────────────────────────────────────┐
│                      Windows Desktop — GolfSimVision                   │
│                                                                        │
│  Video Receiver → Frame Buffer (timestamped, monotonic)                │
│  Calibration (homography: pixels → mm on the putting plane)            │
│  Ball Detection → Tracking → Shot State Machine                        │
│  PuttMeasurement {t, ball_speed, HLA, confidence, reject_reason}       │
│  Replay Buffer + Clip Extraction                                       │
│  ── Shot Model (protocol-neutral) ──                                   │
│  Simulator Connector (GSPro OpenConnect)   ← pluggable                 │
│  Launch Monitor Connector (R10)            ← pluggable, see §5         │
│  UI / Diagnostics / Session Recorder                                   │
└──────────┬──────────────────────────────────────┬──────────────────────┘
           │ TCP 921 JSON                         │ ??? — see §5
           ▼                                      ▼
      ┌─────────┐                         ┌──────────────┐
      │  GSPro  │──── 201 Player ────────▶│  Garmin R10  │
      └─────────┘   (Club="PT" is the     └──────────────┘
                     ONLY mode signal)
```

The single most important structural note: **club information flows simulator → monitor, one way only.** GolfSimVision cannot tell GSPro "the user is putting now." GSPro tells us. That is confirmed in both independent OpenConnect implementations and in your own logs.

---

## 3. REQUIREMENTS / VERIFIED / ASSUMPTIONS / EXPERIMENTS / DECISIONS

### 3.1 REQUIREMENTS (from you, not negotiable without your say-so)

- Windows desktop app is the coordinator; iPhone is a camera/sensor.
- GSPro via OpenConnect; Garmin R10 user first; connector interfaces kept pluggable.
- Putting measurement is the MVP. Replay is next. Full ball-flight measurement is explicitly **not** in scope.
- Reliable measurement > image quality. Low latency > pretty pictures.
- Runtime format negotiation — never hard-code 240 FPS.
- LiDAR must not become a dependency.
- Preserve V1's visual design; discard V1's engineering.
- Precision over recall: a dropped putt beats a wrong putt, and rejections must be visible to the user with a reason. *(Carried forward from V1's CONTEXT.md — confirm you still hold this.)*

### 3.2 VERIFIED FACTS (I checked these; each has a source)

**GSPro Open Connect V1 — from the official spec:**
- TCP **921**, `127.0.0.1`, **no authentication**, assumes same PC.
- Required root: `DeviceID` (string), `ShotNumber` (int), `APIversion` (string `"1"` — **lowercase v** in the spec), `Units` (`"Yards"`).
- `BallData` **required**: `Speed`, `SpinAxis`, `TotalSpin` *(or* `BackSpin`+`SideSpin`*)*, `HLA`, `VLA`. `CarryDistance` optional.
- `ClubData`: all ten fields optional.
- `ShotDataOptions`: `ContainsBallData`, `ContainsClubData` required; `LaunchMonitorIsReady`, `LaunchMonitorBallDetected`, `IsHeartBeat` optional. **The spec spells it `LaunchMonitorBallDetected` correctly** — the Java project's `LaunchMontiorBallDetected` is that project's own typo.
- Response codes: **200** shot received, **201** player information, **501/5xx** failure.

**GSPro 201 Player payload — verified empirically from your own V1 session logs**, which is stronger evidence than the spec (the spec only shows `Handed` and `Club`):
```json
{"Code":201,"Message":"GSPro Player Information",
 "Player":{"Handed":"RH","Club":"PT","DistanceToTarget":3.3,"Surface":null}}
```
Therefore, settled:
- **The putter club code is `"PT"`**, not `"P"`. (gspro-connector's Java enum says `"P"` and is wrong.)
- **`DistanceToTarget` is real and populated** (3.3, 5.4 yards observed). This is a free, high-value signal — it's a plausibility oracle for a measured putt and a distance display for the UI. No reference project uses it.
- **`Surface` exists but is always `null`** in practice.
- **`IsOnGreen` does not exist.** V1 has a documented bug from assuming it did.
- GSPro accepted `"APIVersion"` (capital V) from gspro-r10.exe, so its JSON parsing appears case-insensitive — but we should send the spec spelling.

**Framing:** OpenConnect has **no delimiter and no length prefix**. Both independent implementations converged on brace-depth framing. OpenFlight's `find_json_end()` is the correct version — string-and-escape aware, with a 64 KB resync guard. Two JSON objects **do** arrive in one `recv()`, and one object **does** split across two. This is tested in OpenFlight and must be handled.

**ShotNumber:** signed 32-bit. Epoch **milliseconds overflow it and GSPro replies 501 "Bad format."** OpenFlight seeds from epoch **seconds** so the sequence stays monotonic across restarts and under 2³¹−1. This is three separate bugs encoded in one design decision; take it.

**The HaishinKit prototype in the Appendix cannot work.** `RTMPServer` does not exist in HaishinKit — not in the current release (2.2.5), and not at tags 1.2.0, 1.4.0, 1.6.0, 1.8.0, 1.9.0 or 2.0.0. There is no RTSP support of any kind. HaishinKit is a *publisher* library: the phone dials out to a server, it never listens. `startServer(port:)` does not exist. The prototype's entire transport topology is fictional and must be deleted rather than debugged. *(Anything else in that appendix produced the same way deserves the same scrutiny.)*

**iOS capture:**
- 240 FPS is a **1080p** format on current iPhones, not 720p — that constraint is from the iPhone 6/6s era.
- `kCVPixelFormatType_OneComponent8` is not offered by capture devices, **but you don't need it**: `420v`/`420f` are bi-planar and plane 0 *is* a full-resolution 8-bit grayscale image. `CVPixelBufferGetBaseAddressOfPlane(pb, 0)` gives zero-copy grayscale straight into a `CV_8UC1` Mat. The Appendix's "memset the chroma plane" trick is unnecessary work.
- At 240 FPS the delegate budget is **4.17 ms/frame**. `alwaysDiscardsLateVideoFrames` defaults to `true`, so overruns are silently dropped. The only visibility is `captureOutput(_:didDrop:from:)` + `kCMSampleBufferAttachmentKey_DroppedFrameReason`.
- Setting `activeFormat` forces the session to `inputPriority` and disables automatic configuration. Setting a frame duration outside the format's range raises `NSInvalidArgumentException`.
- `setExposureModeCustom` with a long duration **silently lowers the frame rate**. Apple's own example is exactly our case: lock shutter, auto ISO. Re-read `activeVideoMinFrameDuration` after setting exposure.
- Apple documents the thermal mechanism (`systemPressureState`, `videoDeviceNotAvailableDueToSystemPressure`) and prescribes frame-rate throttling as the mitigation, but publishes **no durations**. Must be measured.

**Bandwidth (computed):** 720p240 Y-plane only = **1.77 Gbit/s** raw. 1080p240 Y-only = **3.98 Gbit/s**. Raw streaming at capture rate is off the table over any link available.

**OpenCV ingest:** `cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)` is a **no-op on the FFmpeg backend** — the property is never handled in `cap_ffmpeg_impl.hpp`. This is the most widely repeated bad advice on this topic. `OPENCV_FFMPEG_CAPTURE_OPTIONS` is real but uses `;` as key/value separator and `|` as pair separator, and setting it replaces the default `rtsp_flags=prefer_tcp`.

**iOS distribution:** free personal Apple ID → provisioning profiles expire in **7 days**, 3 devices, no TestFlight. V1 hit this (`"needs re-signing roughly weekly"`). $99 Developer Program → TestFlight, 100 internal / 10,000 external testers, 100 iPhones/year for ad-hoc.

**The R10 connector V1 was built around (`gspro-r10.exe` v2.1.1, .NET 7, 32feet Bluetooth) already does most of what the kickoff asks for.** From its `settings.json`:
- owns the GSPro OpenConnect socket (`openConnect: 127.0.0.1:921`)
- talks to the R10 **directly over Bluetooth** (`bluetoothDeviceName: "Approach R10"`), with `autoWake: true`, `calibrateTiltOnConnect`, `reconnectInterval`, and an environment model (altitude, humidity, temperature, air density, tee distance)
- **also** runs an E6 server on 2483
- **has a putting receiver on port 8888** that expands a 3-field putt into a full OpenConnect message
- auto-launches and closes the putting camera app based on club selection (`onlyLaunchWhenPutting: true`)
- `sendStatusChangesToGSP: false // does nothing as of today` — i.e. **launch-monitor readiness reporting to GSPro is a no-op**

That last bullet set is the single most consequential finding in this review. See §5.

**Prior-art accuracy reality:**
- `cam-putting-py` derives scale as `pixels_per_mm = detected_ball_radius_px / 21.33mm`, re-derived per putt, with no calibration step and no reference object. Speed comes from **exactly two samples across a 20-pixel gate**, timed with Python wall-clock. A 64-deep point buffer exists and is never used for a fit. Its one recorded test log shows **9 of 17 putts clean (53%)** — 3 false triggers, 1 putter-detected-as-ball, 4 no-reads. There are no published speed or HLA accuracy numbers anywhere. The author's response to accuracy problems on the `spin` branch was a 300 FPS industrial camera with **byte-identical maths**.
- **OpenFlight's camera never solves scale on its own.** It borrows range from mmWave radar and speed from Doppler radar. Their own docstring: *"the tested down-the-line view cannot independently measure downrange speed."* A single iPhone has neither radar.
- OpenFlight's shot trigger is a **hardware acoustic sensor**, not vision. There is no ball-ready detection, no re-arm logic, and no duplicate suppression anywhere in it.
- OpenFlight rejects any shot under **35 mph**, and `PT` is deliberately mapped to `UNKNOWN`. If we reused its resolver unchanged, a putt would reach GSPro as a **5000 rpm, 18°-launch** shot.

### 3.3 ASSUMPTIONS the documents make that I want to challenge

**A1. "Stream high-FPS video continuously from phone to PC."** I think this is the wrong shape. A putt is a ~1 second event inside a session of minutes; continuous 240 FPS streaming ships ~98% of its pixels to discover nothing happened, and it buys you: a thermal ceiling, Wi-Fi jitter sensitivity, codec artefacts on the exact object you're measuring, and a 4.17 ms PC-side budget. **Buffer on the phone, trigger on motion, ship a ~0.5 s window.** Your own V1 provides the proof: the iOS latency fix had to special-case `TRACKING` and stop skipping frames *specifically because* "smooth live preview" and "dense enough sampling to measure a putt" are conflicting requirements on one stream. Those are two streams. Treat them as two streams.

**A2. "240 FPS is the target."** Interrogate this. A putt is 3–10 mph (1.3–4.5 m/s). At 120 FPS the ball moves 11–37 mm per frame across a ~1 m ROI — that's 25–90 usable track points. That is *plenty* for a least-squares velocity fit. Neither cam-putting-py's nor V1's accuracy problems were frame-rate problems; they were **scale problems and two-sample-estimator problems**. 240 FPS is genuinely needed for *clubhead* at full-shot speed, which is Phase 3/5. Proposal: **make 120 FPS the MVP target and treat 240 as a Phase 3 unlock.** This materially de-risks transport, thermals and the PC budget.

**A3. "Camera calibration is a detail."** It is not — it is the whole ballgame, and no reference project solves it. Everything downstream is a linear multiple of mm-per-pixel. cam-putting-py's ±1 px error on a ~10 px radius is a **±10% speed error**, and V1 shipped a ppm-inflation bug that made every putt too slow by a constant factor, producing exactly the "ball barely creeps forward" complaints in your spec. Proposal: **a one-time homography from a printed calibration target on the mat**, giving true mm/px on the putting plane, independent of ball detection, perspective-correct across the whole ROI, and re-verifiable. Ball radius becomes a *cross-check*, not the ruler.

**A4. "GolfSimVision can detect launch-monitor readiness and wake the R10."** The connector's own config says readiness reporting to GSPro does nothing. And `autoWake` for the R10 exists — *in the connector*, over Bluetooth. Whether we get it depends entirely on §5.

**A5. "GSPro acknowledges shots."** Code 200 is in the spec, but your V1 CONTEXT.md states GSPro gives no shot-specific ack distinguishable from other status traffic, and OpenFlight never populates `shot_number` on its acks and has no code path that ever produces a failed one. **Design for no confirmation.** Do not build a UI that claims "Registered."

**A6. "The desktop stack is an open question."** It is, but V1 answers it by counter-example: Electron + Next.js + Python/FastAPI is three runtimes, two languages and a dev-script launcher, and your own reliability plan named the root cause: *"the only way this app currently runs is via its development startup scripts… that single fact is the root cause of today's connection bug and the entire reason it isn't ready to hand to a less technical person."*

**A7. "V1's UI design is a constraint."** It's an asset, and a cheap one. The design is CSS custom properties, three Google fonts, a radius ladder and a three-zone layout. It is not framework-bound. It survives a move to WPF, Avalonia, Tauri or anything else. Do not let "preserve the look" drag the framework choice.

### 3.4 EXPERIMENTS — see §8

### 3.5 DECISIONS — see §10

---

## 4. Review: the iPhone → Windows camera concept

**The concept is sound. The proposed implementation is not.**

What's wrong with it as written:
- The transport library doesn't have the API the prototype calls (verified 404 at every tag).
- The topology is backwards. HaishinKit can only push; a phone-hosted RTSP server isn't a thing it does. If we used RTMP/SRT at all, the **PC** would run the listener and the phone would dial out — which is the better topology anyway, since the PC is the stable endpoint.
- RTSP/H.264 → `cv2.VideoCapture` is the worst available ingest: it stacks encoder GOP/lookahead, RTP reorder queue, FFmpeg probe/analysis, decoder buffering, and a synchronous YUV→BGR convert (663 MB/s of memory traffic at 720p240 before any CV work).
- H.264 rate control systematically starves a small, fast, high-contrast object against a static background — precisely what we're measuring. Sub-pixel centroid estimation is what's at risk, and DCT ringing biases centroids *systematically*, not randomly.

**What I'd propose instead:**

| Layer | Choice | Why |
|---|---|---|
| Capture | `AVCaptureVideoDataOutput`, Y-plane only, locked short manual exposure | zero-copy grayscale, no conversion, blur controlled at the sensor |
| Buffer | on-phone RAM ring, ~0.5–1 s | decouples capture rate from network rate entirely |
| Trigger | cheap on-device ROI frame-diff (vImage/Accelerate, well under 4 ms) | 98% of frames never leave the phone |
| Measurement transport | `NWListener` (first-party TCP), **lossless or MJPEG** burst of the stroke window | no codec in the measurement path |
| Preview transport | separate low-rate (~30 FPS), lossy, best-effort, always-newest-frame-wins | alignment and replay don't need fidelity |
| Wire | **`iproxy` over USB** (libimobiledevice; `usbmuxd` ships with Apple Mobile Device Support on Windows), Wi-Fi as fallback | point-to-point, no router, no cellular plan, no RF environment to fight — same protocol either way |

This dissolves the thermal ceiling, the jitter sensitivity, the codec-artefact risk and the 4.17 ms PC budget in one move, and makes the transport choice nearly irrelevant. The cost is that the measurement appears ~0.5 s after the putt — which is fine. You are reporting a number to GSPro, not driving a live video wall.

**Caveats to prove:** that `iproxy` reaches a third-party app's `NWListener` on a stock device (high-confidence inference from the documented usbmux mechanism; one-hour spike), and usbmux throughput — it may be pinned to USB 2 (480 Mbit/s), which is ample for a burst and not for continuous raw.

**Open risk:** which lens exposes 240 FPS (probably wide-only, unverified), and `minExposureDuration` values. Both resolved by a 15-line enumeration — E1 below.

---

## 5. Review: Garmin R10 → GolfSimVision → GSPro

**This is the highest-stakes decision in the project, and V1 already ran the experiment.**

The reference architecture you were using — and which ADR-0001 calls *"proved reliable in practice"* — is: **`gspro-r10.exe` owns everything.** It holds the Bluetooth link to the R10 (with auto-wake and tilt calibration), it owns the GSPro OpenConnect socket, and it accepts putts over HTTP on port 8888, expanding them into full OpenConnect messages. The putting camera is a dumb sensor that POSTs three numbers.

V1 moved away from that to "direct" mode — GolfSimVision owns 921 itself — for a **product** reason, stated plainly in ADR-0001: *"trading the bridge's proven reliability for a single-process, plug-and-play experience (the core product goal: run one program, not two)."* And the ADR is honest that the reliability complaints were later traced to track quality, not the connector.

That's a legitimate decision. But it has a consequence the kickoff doesn't price in: **if GolfSimVision owns the GSPro connection, it must also replace everything else gspro-r10.exe does** — R10 Bluetooth protocol, auto-wake, tilt calibration, environment model, reconnect. That is not a connector; that is a second product.

Four options, honestly stated:

| | Model | We must build | User runs | Notes |
|---|---|---|---|---|
| **A** | **Relay.** gspro-r10.exe owns 921; GolfSimVision POSTs putts to its :8888 | almost nothing on the LM side | 2 apps | Proven. Fastest path to a working MVP. Contradicts "one program." Depends on a third-party binary. |
| **B** | **Co-client.** Both GolfSimVision and gspro-r10.exe connect to 921 | OpenConnect client | 2 apps | **Unverified that GSPro accepts two clients.** V1's logs show session churn here. Must be tested (E0). |
| **C** | **Full ownership.** GolfSimVision does R10 Bluetooth itself | BLE protocol, wake, tilt, env model, OpenConnect | 1 app | The goal. Largest scope. The Java project died on exactly this rock — though gspro-r10 and gspro-garmin-connect-v2 are open source, so the protocol is already reverse-engineered and readable. |
| **D** | **E6 proxy.** GolfSimVision runs the E6 server on 2483; Garmin Golf phone app feeds it | E6 handshake (V1 has working code), OpenConnect | 1 app + Garmin Golf on a phone | V1's `e6_direct` mode. Working code exists in `backend/monitors/r10.py`. Requires the phone app running, which is another thing to go wrong. |

**My recommendation: build the connector seam so that A, B and D are all configurations of the same architecture, ship A or D first, and treat C as a later goal with its own Phase.** Concretely: GolfSimVision always produces a protocol-neutral `PuttMeasurement`; a `SimulatorConnector` interface has two implementations — `GSProDirect` (TCP 921) and `GSProViaR10Connector` (HTTP :8888) — and which one is active is one config line. That's cheap now and expensive to retrofit.

**Mode switching is settled and simple.** `Club == "PT"` in the 201 Player push is the trigger. It's verified in your logs. `IsOnGreen` does not exist; don't code against it. Manual override persists until cleared. Fringe/chip is exactly what the override is for. `DistanceToTarget` comes along free and is worth using as a plausibility check on the measured putt.

---

## 6. What to take from OpenFlight, and what not to

**Take (these are genuinely good and directly reusable):**

1. The **codec / transport / resolver seam**. A five-method `Codec` protocol (`build_shot`, `parse_inbound`, `heartbeat_bytes`, `on_connect_bytes`, `fields_for_target`) over one shared TCP transport. Adding a second simulator is one file.
2. **`find_json_end()`** — string-and-escape-aware brace framing with a 64 KB resync guard. Mandatory, and the Java version's naive brace count is buggy (breaks on `{` inside a string).
3. **ShotNumber discipline** — epoch-seconds seed, monotonic across restarts, ≤ 2³¹−1, allocated exactly once per physical shot, and **not allocated at all while no connector is connected** so the sequence doesn't drift.
4. **`ConnectionError` (an `OSError` subclass) on send-while-disconnected**, so the shot pipeline's guard catches the TOCTOU race instead of it propagating.
5. **`CONNECTING` vs `RECONNECTING` as distinct user-visible states** — "connecting forever" means wrong IP or blocked port; "reconnecting" means it worked once. This single distinction carries their whole troubleshooting doc.
6. **Heartbeat suppressed by real traffic** (real sends update `last_send`; heartbeats don't), and no heartbeat thread at all when the protocol has none.
7. **Per-field measured/estimated provenance with UI badges.** We will be *fabricating* `VLA`, `SpinAxis` and `TotalSpin` for every putt. Being honest about that in the UI is the right call and costs nothing.
8. **Withhold-with-a-reason quality gating** — machine-readable `rejected_*` statuses and confidence tiers, rather than emitting a bad number. Aligns exactly with your precision-over-recall decision.
9. **Save raw frames unrotated, plus an offline replay harness.** Every algorithm and calibration change gets re-scored against recorded shots instead of requiring you to go hit putts. Adopt this on day one.
10. **Spatial and apparent-size priors are what made their ball detection robust** — not a better detector. A putting ball sits in a known region at a known apparent size. Use that.
11. **Exposure as a fixed validated ladder, chosen at startup and then locked** for the session. No continuous AE. Their guidance: *add light rather than lengthening shutter or raising gain.*

**Don't take:**

1. Their camera architecture. It is a Pi 5 + patched OV9281 kernel driver, triggered by a hardware acoustic sensor, borrowing scale from radar. It shares essentially nothing with iPhone → Windows.
2. The YOLO path. `docs/development/camera-yolo.md` says it outright: *"The server does not integrate this YOLO tracker."* It's a benchmark script with no published FPS, no integration and no tests.
3. `min_ball_speed_mph = 35.0` and the "up and away" trajectory prior — both actively wrong for putting.
4. The resolver's fallback table unbranched — `UNKNOWN` club yields 5000 rpm / 18° VLA.
5. Their ~2 s shot→sim latency budget; that's a serial-dump artefact of the radar. Nothing in that codebase was designed for a low-latency vision path.

**What OpenFlight does not give us, and nobody else does either:** ball-at-rest detection, arming, vision-based triggering, re-arm after a putt, and duplicate suppression for a camera trigger. That is *our* problem to solve, and per V1's field testing it is where the real failures live (see §9, risk 4).

---

## 7. Preserving V1's design without inheriting V1's engineering

This is the easy part, and the extraction is already done. V1's visual language is:

- **Palette:** `--color-bg:#0a1628`, `--color-surface:#0f2040`, `--color-surface-2:#162a4a`, borders `rgba(255,255,255,.06/.12)`, text `#e2e8f0`, muted `#64748b`; semantic green `#22c55e` (connected / putting / start zone), amber `#f59e0b` (gateway / warning / calibrating), blue `#3b82f6` (full-shot / active tool), red `#ef4444` (detection / destructive).
- **Three fonts, three jobs:** Barlow Condensed (all uppercase wide-tracked labels, buttons, badges), DM Sans (body), JetBrains Mono with `tabular-nums` (every measurement). The mono stat value is the signature element.
- **Surfaces:** `.panel` = surface + 1px hairline + 8px radius + `inset 0 1px 0 rgba(255,255,255,.04)` top-edge highlight. Radius ladder 3/4/5/6/8/full.
- **Layout:** header (wordmark · connection pill · settings) / 288 px left sidebar (mode indicator → connections → last-putt hero → shot log) / camera-as-hero main area with a canvas overlay vocabulary (green start zone, amber dashed gateway, red ball circle + track polyline, corner crosshairs, vignette) over a status strip and a 60 px toolbar / 320 px right settings drawer.
- **Motion:** expo-out `cubic-bezier(.16,1,.3,1)` entrances, 0.15 s hover transitions, pulsing ring on the active mode, ping halo on connected dots.

**Plan:** write `docs/UI_DESIGN.md` from this as a framework-neutral spec — tokens, type scale, component inventory, layout geometry, interaction behaviour — with V1 screenshots as reference plates. Then the new app implements against that document, not against V1's code. Nothing here depends on React, Tailwind, Next or Electron.

Two V1 UI behaviours worth carrying as *requirements*, not just styling:
- The zone-setup scrim is deliberately semi-transparent because *"the user needs to SEE the camera to position their zones accurately."*
- Overlays are drawn in exactly one place. V1 drew zones both server-side into the JPEG and client-side on canvas, and shipped duplicate boxes to users.

---

## 8. Phase 0 — the validation experiments

Every one is **ASSUMPTION → EXPERIMENT → MEASUREMENT → DECISION**. Ordered by what unblocks the most.

| # | Assumption under test | Experiment | Measurement | Unblocks |
|---|---|---|---|---|
| **E0** | We know how GSPro behaves | **Synthetic shot injector** — a fake launch monitor, no camera. Steal gspro-connector's `FormLaunchMonitor` idea. | Does GSPro accept a putt with `VLA=0, SpinAxis=0, TotalSpin=0`? Does it require `LaunchMonitorIsReady` first? Does it ack per-shot? **Does it accept two simultaneous clients on 921?** What exactly does `DistanceToTarget` do across a round? | §5 decision, the whole connector |
| **E1** | The iPhone supports the formats we want | 15-line `AVCaptureDevice` format enumeration on the target phone(s) | Per lens: dimensions × max FPS, `minExposureDuration`, `maxISO`, `isVideoBinned`, `unsupportedCaptureOutputClasses`, stabilization support, `availableVideoPixelFormatTypes` after setting `activeFormat` | Camera spec, A2 |
| **E2** | USB gives us a clean wired path | `iproxy` on Windows → `NWListener` in a stub iOS app, round-trip a payload | Works at all? Throughput? Latency? Does it survive sleep/wake and cable reseat? | Transport decision |
| **E3** | The phone can sustain capture | 20-minute capture run at the E1-chosen format | Delivered FPS, drop count and `DroppedFrameReason` histogram, `systemPressureState` timeline, time-to-throttle | A1, A2 |
| **E4** | Compression doesn't hurt measurement | Capture one putt as raw Y **and** H.264. Run the same centroid estimator on both. | Difference in derived speed and HLA | Codec decision |
| **E5** | We can solve scale properly | Printed calibration target on the mat → homography → roll a ball a **known distance at a known speed** (ramp with fixed release height) | mm/px accuracy across the ROI; measured vs known speed error | A3 — the biggest risk |
| **E6** | We can iterate offline | **Record a putting corpus**: 50+ real putts, raw frames, multiple speeds/lines/surfaces/lighting, with hand-labelled ground truth. Include adversarial cases: hand placing the ball, putter head in the ROI, feet, shadows, second putt immediately after the first. | The corpus itself | Everything. **Do this early — it is the asset every later decision is scored against.** |
| **E7** | The R10 path works as assumed | Run gspro-r10.exe and a second OpenConnect client together; then separately drive V1's E6 server on 2483 from the Garmin Golf app | Which topologies actually survive a round | §5 decision |

**Exit criteria for Phase 0:** a chosen camera format with measured sustained FPS; a chosen transport with measured latency; a verified GSPro putt payload that GSPro visibly accepts; a written decision on connection ownership; a calibration method with a measured error bound; and a recorded putting corpus.

Note what is *not* in Phase 0: LiDAR, YOLO, the desktop UI, replay. None of them are on the critical path.

---

## 9. The five biggest technical risks

**1. Pixel → real-world scale.** Everything downstream is a linear multiple of it, and **no reference project solves it from a single camera.** cam-putting-py uses the ball's own detected radius (±1 px on ~10 px = ±10% speed error). OpenFlight borrows range from radar and says plainly its camera can't measure downrange speed alone. V1 shipped a ppm-inflation bug that made every putt slow by a constant factor — which is exactly the "ball barely creeps forward" symptom in your own reliability spec. *Mitigation: E5, homography from a fixed calibration target; ball radius demoted to a cross-check.*

**2. Who owns the GSPro connection — and therefore how much of gspro-r10.exe we must rebuild.** This is scope-defining, not a detail. Owning 921 means owning R10 Bluetooth, auto-wake, tilt calibration and an environment model, or requiring the Garmin Golf phone app. V1 made this call for a product reason and it is the reason V1 grew. *Mitigation: E0 + E7, and a connector seam that makes it a config line.*

**3. The camera data model — continuous stream vs buffered burst.** The prototype's transport doesn't exist, the bandwidth maths rules out raw streaming, and your own V1 latency fix is empirical proof that one stream can't serve both preview and measurement. This is the decision that determines whether the phone side is hard or easy. *Mitigation: A1/A2 above, E2/E3/E4.*

**4. False triggers, and re-arming for the second putt.** V1's two highest-severity field bugs were *"hand movement or putter reflection triggers detection and fires a shot before the ball is even placed"* and *"second putt in same session does not register."* cam-putting-py logged 53% clean. Both projects have a written-but-never-built ARMING/countdown plan. No reference project solves it. *Mitigation: an explicit shot state machine (`at-rest → armed → moving → measured → re-arm on at-rest`) designed up front against the adversarial cases in the E6 corpus, plus V1's precision-over-recall rejection gate with a visible reason.*

**5. Productization.** V1 died here, not in the algorithms. Your own words: *"the only way this app currently runs is via its development startup scripts… that single fact is the root cause of today's connection bug and the entire reason it isn't ready to hand to a less technical person."* Plus: manual firewall rules, manually typed LAN IPs, a 2.5 GB CUDA reinstall on every launch, and a free-tier iOS profile that expires weekly. *Mitigation: decide the desktop stack and packaging story before writing UI, and buy the $99 Apple Developer account now if anyone but you will ever run this.*

---

## 10. What I recommend we decide first — before any code

Three decisions, in order. Each one changes what the others look like.

### D1 — GSPro connection ownership (§5)
Pick A, B, C or D, with eyes open about scope. My read: **build the seam for all of them, ship A or D for the MVP, treat C as its own phase.** This needs your product judgement more than my technical judgement — it's the "one program, not two" question, and you already made this call once.

### D2 — Camera data model (§4, A1/A2)
**Buffered burst + separate low-rate preview**, or **continuous stream**? And is the MVP target **120 FPS** or **240 FPS**? I'd argue burst + 120, with 240 as a Phase 3 unlock. If you disagree, I want to hear why — it may be that you want the replay feature to feel cinematic, which is a real reason to want 240 and changes the calculus.

### D3 — Windows desktop stack (§3.3 A6)
Not decided anywhere yet, and it constrains everything. The candidates, honestly:

| | Pros | Cons |
|---|---|---|
| **.NET 8 + Avalonia/WPF, OpenCvSharp + ONNX Runtime** | one process, one signed installer, native Windows, matches gspro-r10's stack, no Python/CUDA fragility, good threading | you'd be writing C#, not your stack |
| **Python (CV) + Tauri/native shell, properly packaged** | keeps OpenCV/Ultralytics ergonomics you already know, V1's detection code is portable | still two runtimes; PyInstaller + CUDA packaging is exactly what bit V1 |
| **Rust core + thin UI** | fastest, single binary, no GC pauses in the capture path | slowest to write, smallest ecosystem for golf CV |

The V1 design language survives all three, so this is purely an engineering call.

**Two things I'd also like your answer on, which aren't decisions so much as confirmations:**
- Is **precision over recall** still the product stance? (Drop a doubtful putt rather than send a wrong one, and tell the user why.)
- Is the **$99 Apple Developer account** in scope? It's a yes/no that determines whether an iPhone app is a product or a personal tool.

---

## 11. Repository and docs structure (for when we package for Claude Code)

Close to your kickoff's proposal, with the research docs promoted because they're the ones that prevent re-litigating settled questions:

```
GolfSimVision/
├── README.md
├── CLAUDE.md                        # working agreements, invariants, known regression patterns
├── docs/
│   ├── PROJECT_BRIEF.md
│   ├── ARCHITECTURE.md              # + ADRs for D1/D2/D3
│   ├── adr/
│   ├── UI_DESIGN.md                 # V1 design language, framework-neutral
│   ├── GSPRO_OPENCONNECT.md         # verified protocol facts (§3.2)
│   ├── GSPRO_PUTTING.md             # the putt payload, once E0 confirms it
│   ├── R10_INTEGRATION.md           # the four options, the chosen one, why
│   ├── CAMERA_CAPABILITIES.md       # E1 output, per device
│   ├── TRANSPORT_BENCHMARKS.md      # E2/E3/E4 output
│   ├── CALIBRATION.md               # E5 method + measured error bound
│   ├── TEST_PLAN.md
│   └── research/
│       ├── OPENFLIGHT_ANALYSIS.md
│       ├── OPENFLIGHT_GSPRO_ANALYSIS.md
│       ├── CAM_PUTTING_PY_ANALYSIS.md
│       └── V1_POSTMORTEM.md         # what V1 got right, what it got wrong, why
├── ios/GolfSimVisionCamera/
├── desktop/GolfSimVision/
├── shared/protocol/                 # wire format, shared by both sides
├── prototypes/                      # E0–E7 harnesses; stays until architecture is frozen
├── corpus/                          # E6 recordings + labels (git-lfs or external)
└── tests/
```

Much of `docs/research/` and `docs/UI_DESIGN.md` can be written **now**, from this review, before a line of application code exists.

---

## 12. Stopping here

I have not written any application code and I have not packaged anything for Claude Code.

The next move is yours: **D1, D2, D3**, plus the two confirmations. Once those are settled I'll turn them into ADRs, write the Phase 0 harness specs, and package the repo.
