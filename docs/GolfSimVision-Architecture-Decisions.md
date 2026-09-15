# GolfSimVision — Architecture Decisions & Phase 0 Plan

**Date:** 2026-09-15
**Status:** Decisions agreed with Blake. Phase 0 not yet started. No application code written.
**Companion:** `GolfSimVision-Project-Readiness-Review.md` (evidence base for everything below)

---

## The constraint that shapes everything

**GSPro's OpenConnect port 921 accepts one client at a time.** (Confirmed by Blake from direct experience.)

This is not a minor detail — it is the hinge the whole architecture turns on:

- If GolfSimVision owns the 921 socket, `gspro-r10.exe` **cannot connect**.
- Therefore full shots stop working unless GolfSimVision receives R10 data itself.
- Therefore **R10 ingress is not a later phase. It is required for v1 to be usable for a round of golf.**

The putting pipeline can still be *developed and tested* standalone — no R10 involvement needed to prove a putt reaches GSPro. But "usable product" = GSPro connector + R10 ingress + putting, all three.

---

## ADR-0001 — GolfSimVision owns the GSPro OpenConnect connection

**Status:** Accepted

**Decision.** GolfSimVision opens and owns the single TCP connection to GSPro on port 921. It does not relay through `gspro-r10.exe` or any other third-party process.

**Context.**

The alternative was relaying putts to `gspro-r10.exe`'s HTTP putting receiver on port 8888 — the architecture V1's ADR-0001 called *"proved reliable in practice."* Three things ruled it out:

1. **It inherits defects we cannot fix.** Blake observed that GSPro's launch-monitor readiness indicator never updates. Root cause found in the session logs: every heartbeat `gspro-r10.exe` sends carries `"LaunchMonitorIsReady": false` — 4 of 4 observed, never once `true`. Its own `settings.json` confirms the feature is inert: `"sendStatusChangesToGSP": false, // does nothing as of today`. Owning the socket fixes this with one boolean.
2. **The protocol is trivially ownable.** ~300 lines against a spec we have read, cross-checked against two independent implementations, and verified empirically against Blake's own logs.
3. **Commercial.** GolfSimVision is to be sold direct (ADR-0008). Shipping a paid product whose core data path depends on a third party's free closed binary is not a supportable position.

**Consequences.**

- We get readiness reporting, correct `ShotNumber` discipline, real connection-state semantics, and no external process dependency.
- We inherit responsibility for R10 ingress (ADR-0002) — unavoidable given the single-client constraint.
- Verified protocol facts to implement (see Readiness Review §3.2): TCP 921; brace-balanced framing, string-and-escape aware, 64 KB resync guard; `APIversion` as string `"1"`; `ShotNumber` signed 32-bit seeded from epoch **seconds**; `BallData` requires `Speed`, `SpinAxis`, `TotalSpin`, `HLA`, `VLA`; codes 200 / 201 / ≥500.

---

## ADR-0002 — R10 ingress via E6 Connect for v1; Bluetooth-direct in v2

**Status:** Accepted

**Decision.** v1 receives Garmin R10 data by running an E6 Connect server on TCP 2483, which the Garmin Golf phone app connects to. Native Bluetooth LE to the R10 is the committed destination but ships in v2.

**Context.**

Bluetooth-direct is the better end state — no phone app, no second device on WiFi, fewer setup steps, which matters for "simple to set up." Blake's preference was to prioritize it. Sequencing argues otherwise:

- **No open-source reference exists.** Every working open project (`fairway-bridge`, `gspro-garmin-connect-v2`) uses E6. The one project that attempted native BLE abandoned it: *"Couldn't end up reverse engineering the Bluetooth communication."* The only working BLE implementation available is a closed .NET binary.
- **We already have working E6 code.** V1's `backend/monitors/r10.py` has a proven handshake: `Handshake` → `Challenge` → `Authentication` → `SimCommand/Ping` every 10 s, newline-ish JSON, `SendShot` → `ACK`.
- **E6 remains valuable after v2 ships** as a fallback for users whose Bluetooth misbehaves, and as a test harness that needs no R10 hardware present.

**Consequences.**

- v1 requires the Garmin Golf app running on a phone on the same network. This is a documented setup step, not a hidden one.
- v2 BLE work gets its own spike with its own risk budget. Route: sniff BLE traffic between the Garmin Golf app and the R10 for interoperability. We do **not** decompile third-party binaries for a commercial product.
- The `LaunchMonitorConnector` interface must be designed now so E6 and BLE are peers, not a retrofit.
- Setup friction from V1 must be designed out: auto-detect and display the LAN IP in-app, and install the firewall rule from the installer. V1's single worst field failure was Blake typing `192.168.5.90` when the PC was on `192.168.4.90`.

---

## ADR-0003 — Buffered-burst camera model; 120 fps measurement target

**Status:** Accepted

**Decision.** Frames are buffered at the source and a window around the stroke is shipped for measurement, rather than streaming continuously at capture rate. MVP measurement target is **120 fps**. Live preview is a separate, lower-rate, best-effort stream.

**Context.**

- Raw 720p240 luma is 1.77 Gbit/s — not streamable over any available link.
- H.264 rate control systematically starves a small, fast, high-contrast object against a static background, which is precisely what we measure. Sub-pixel centroid bias from DCT ringing is *systematic*, not random.
- V1 proved the conflict empirically: its iOS latency fix had to stop skipping frames specifically during `TRACKING`, because *"skipping ahead there starves it down to 0-1 track points and the putt gets silently rejected."* Smooth preview and dense measurement sampling are conflicting requirements on one stream. They are two streams.
- 120 fps yields 25–90 track points across a ~1 m ROI for a 3–10 mph putt — ample for a least-squares velocity fit. Prior-art accuracy failures were **two-sample estimators and scale errors**, not frame rate. cam-putting-py's author responded to accuracy problems with a 300 fps industrial camera and *byte-identical maths*; it did not help.

**Three power states.** The capture session stays alive continuously; only the expensive parts are gated by mode. This avoids session-startup lag when the user picks up the putter.

| State | Session | Frame rate | Ring buffer |
|---|---|---|---|
| Full-shot mode | running | ~30 fps preview | off |
| Putting mode | running | 120 fps | armed |
| Stroke detected | running | 120 fps | shipping window |

**Consequences.**

- Measurement appears ~0.5 s after the stroke. Acceptable — we report a number, not drive a live video wall.
- Replay rate is decoupled from measurement rate. Buttery replay can arrive later by raising capture rate and measuring on every *n*th frame, with no change to the measurement path.
- On iPhone: app must be foreground with `isIdleTimerDisabled = true`. Manual lock or backgrounding kills `AVCaptureSession` (`videoDeviceNotAvailableInBackground`). True of any architecture; burst does not make it worse.

---

## ADR-0004 — Two video sources behind one interface; USB webcam is the reference implementation

**Status:** Accepted

**Decision.** A `FrameSource` interface with two implementations: `UsbWebcam` and `IPhoneCamera`. Both are first-class and supported in the product. The webcam is the **reference implementation** — the one every algorithm is developed and scored against.

**Context.**

- De-risks the entire iOS track. No Apple Developer account, no 7-day provisioning profiles, no App Review on the critical path. (Blake has deferred the $99 account until the app is complete — this decision makes that comfortable rather than risky.)
- Provides a control variable. Same pipeline, two sources, so a bad result can be attributed to camera or algorithm. V1 had no way to do this.
- **Unblocks the putting corpus immediately.** V1's settings confirm Blake's existing webcam already runs **1280×720 @ 120 fps** at 31.5" height with a 78° FOV. Corpus collection can start now.

**On camera quality — recorded because it is counter-intuitive.** For this application, consumer "image quality" is close to irrelevant and 4K is a liability: more pixels means lower achievable frame rate and more processing, and we downscale anyway. What matters is frame rate, locked short shutter (~1/2000 s) to freeze the ball, and shutter type — rolling shutter skews fast-moving objects, global shutter does not.

The iPhone's genuine advantages are its high-FPS formats and real manual exposure control, not resolution. Its well-known low-light advantage comes from computational photography — multi-frame fusion, long exposures, noise reduction — **all of which are disabled in a high-FPS locked-exposure mode.** In the mode we actually need, that advantage largely evaporates.

The correct answer to low light is OpenFlight's: *"Prefer adding light to the hitting area over relying on long shutter times or high gain. Both can reduce clubhead edge quality even when the preview appears bright enough."* A cheap LED panel over the putting area likely buys more accuracy than any camera choice. **P0-5 settles this with data** rather than impression — V1's webcam was running DirectShow auto-exposure (`cam_exposure: -1`, `cam_shutter_speed: -9`), so the earlier comparison was not like-for-like. Blake now has Logi+ shutter/ISO control, making a fair test possible.

**Consequences.**

- Exposure/gain control must be abstracted — DirectShow/MSMF properties for webcam, `setExposureModeCustom` for iPhone.
- Calibration must work for both (ADR-0005).
- iPhone transport: `NWListener` (first-party TCP) reachable over WiFi, or over USB via `iproxy` / usbmuxd. Same wire protocol either way.

---

## ADR-0005 — Calibration by homography; LiDAR for calibration and arming only, never measurement

**Status:** Accepted

**Decision.** Pixel→real-world scale comes from a **one-time homography** computed from a printed calibration target on the putting surface. LiDAR is an optional convenience for calibration, and an optional aid for arming/classification. **LiDAR is never in the measurement path.**

**Context.**

Scale is risk #1 — everything downstream is a linear multiple of it, and no prior project solves it from a single camera:

- cam-putting-py derives `pixels_per_mm = detected_ball_radius_px / 21.33 mm`, re-derived per putt. ±1 px on a ~10 px radius is a **±10% speed error**.
- V1 shipped a ppm-inflation bug that made every putt slow by a constant factor — exactly the *"ball barely creeps forward"* symptom in its own reliability spec.
- OpenFlight's camera never solves scale alone; it borrows range from mmWave radar and Doppler speed. Their own words: *"the tested down-the-line view cannot independently measure downrange speed."*

A homography from a known target gives true mm/px across the whole ROI, perspective-correct, independent of ball detection, and re-verifiable. Ball radius is demoted to a **cross-check**, not the ruler.

**LiDAR reframed.** The kickoff treated LiDAR as a streaming concern and set a Decision Gate around synchronizing depth with high-speed video. That gate is largely moot: the camera is fixed and the mat is flat, so **depth is needed exactly once, at setup.** Capture one depth frame → fit a plane → derive height, tilt and mm/px → switch to the high-FPS format and never touch depth again. No synchronization problem, no frame-rate conflict, no MVP dependency.

**LiDAR's highest-value use is arming, not measurement.** Depth distinguishes a ball on the mat from a hand or putter head ~30 cm above it. At 60 Hz and 256×192 that is useless during a stroke but entirely adequate for *"is there a ball at mat level, and is the zone otherwise clear?"* — which attacks risk #4 directly. V1's worst field bug: *"When the golfer reaches into the start zone to place the ball, hand movement or putter reflection triggers detection and fires a shot before the ball is even placed."*

**On a second camera for redundancy — rejected for v1.** Speed measurement is a timing problem. Two independent devices with independent clocks (one USB, one WiFi) cannot be synchronized to the sub-millisecond precision required without hardware sync. A second unsynchronized camera adds a timing-error source plus a fusion problem. **However**, splitting by *job* rather than duplicating one works, because the jobs have different timing requirements:

| Device | Job | Timing requirement |
|---|---|---|
| Webcam, overhead, 120 fps | Measurement — position over time | sub-millisecond |
| iPhone LiDAR | Arming — ball present, zone clear | ~10 Hz |

**Consequences.**

- A calibration target must be designed and made printable (PDF at known scale, with a printed-size verification step).
- Calibration is versioned and stored; a re-calibration prompt fires if the camera moves.
- Raw frames are saved **unrotated** so calibration can be revised during offline replay (OpenFlight's practice).
- V1's measured `camera_height_inches: 31.5` / `camera_fov_degrees: 78.0` give a sanity cross-check for the homography.

---

## ADR-0006 — .NET 8 + WebView2, single signed executable

**Status:** Accepted

**Decision.** Windows desktop app built on .NET 8 with a WebView2-hosted UI written in HTML/CSS/JS. Ships as one self-contained, signed `.exe`. CV via OpenCvSharp; any ML via ONNX Runtime.

**Context.**

V1's failure was not HTML — it was Electron + Next dev server + Python + a `.bat` launcher, where detection quality depended on whether a browser was open (`if not _yolo_running and len(_ws_clients) > 0`) and *"the only way this app currently runs is via its development startup scripts… that single fact is the root cause of today's connection bug and the entire reason it isn't ready to hand to a less technical person."*

WebView2 keeps the part Blake actually writes — the UI — in his own stack, while removing every runtime-packaging failure mode: no Python, no Node, no bundled interpreter, no 2.5 GB CUDA wheel, no `--reload`. WebView2 is present by default on Windows 11.

The ADR-0002 Bluetooth decision reinforces this: Windows BLE from .NET (WinRT / 32feet) is first-class and is the stack the one working R10 implementation uses; Python's `bleak` is workable but weaker.

**Known risk, accepted.** Blake does not write C# and will rely on Claude Code for the backend. A codebase you cannot read is one you cannot debug under pressure. Mitigations:

- The UI — where most iteration happens — is 100% HTML/CSS/JS.
- OpenCvSharp is a thin, faithful wrapper over OpenCV; Python OpenCV knowledge transfers nearly line-for-line.
- The C# surface is engine work (capture, CV, sockets, BLE) that is written once and changed rarely.
- Phase 0 experiments are written in **Python** — the right language for trying five detectors in an afternoon. None of that code ships. Only the settled pipeline is ported.

**Non-negotiables carried from V1's postmortem, regardless of stack.**

1. One process. No camera + AI + two long-lived sockets in a process that a single OpenCV exception can kill.
2. **Typed messages** on every interface. V1 shipped the same untyped-`dict` bug three separate times.
3. Settings persisted **outside** any watched/reloaded tree. V1's `settings.json` lived where its file watcher could see it, and writing settings restarted the process mid-session.
4. No module-level mutable globals or import-time singletons. V1 needed `os.execl` self-restart and an AST-parsing regression test to compensate.
5. Detection behaves identically whether the UI is open, closed, or refreshed.
6. Overlays drawn in exactly one place. V1 drew zones both server-side into the JPEG and client-side on canvas and shipped duplicates to users.

---

## ADR-0007 — Precision over recall

**Status:** Accepted (reaffirmed from V1)

**Decision.** A dropped putt is preferable to a submitted putt with wrong numbers. Every rejection surfaces a visible, human-readable reason in the UI — not a log line.

**Context.** V1's evidence: sessions where *"Putt sent directly to GSPro"* logged successfully still produced values like **0.6 mph at −30° HLA** — the signature of a 2-point track dominated by detection jitter. Confirmed in logs (`0.65 mph −31.66°`, `0.60 mph −29.80°`, both sent to GSPro).

**Consequences.**

- Machine-readable rejection reasons in the style of OpenFlight's `rejected_*` statuses, with confidence tiers.
- Per-field measured/estimated provenance, shown in the UI. We fabricate `VLA`, `SpinAxis` and `TotalSpin` for every putt; the UI should be honest about that.
- Quality gates (minimum track points, minimum displacement, plausibility bounds) are **not** to be loosened to "show something" without revisiting this ADR.

---

## ADR-0008 — Direct sale from golfsimvision.com

**Status:** Accepted

**Decision.** Distributed and sold direct from the website. Not the Microsoft Store.

**Consequences.**

- **Code signing is required, not optional.** An unsigned installer trips SmartScreen, which is a wall for non-technical buyers. Standard OV certs build reputation slowly; EV certs get immediate SmartScreen trust. Budget for this before first paid release.
- Needs a self-update mechanism (Velopack is the current .NET standard) — decide the install layout now, since retrofitting updates is painful.
- Needs a licensing/activation mechanism.
- No store certification means faster iteration and full ownership of the customer relationship.
- Reinforces ADR-0001: a paid product cannot depend on a third party's free binary in its core data path.
- The iOS companion app remains a separate distribution question, deferred with the Apple Developer account.

---

# Phase 0 — validation plan

Every experiment is **ASSUMPTION → EXPERIMENT → MEASUREMENT → DECISION**. No application code. Python throughout. Everything lives under `/prototypes`.

Ordered by what unblocks the most, revised now that the decisions have collapsed several unknowns. *(The original E0 question "does GSPro accept two clients?" is answered — it does not — which is why R10 ingress moved forward in priority.)*

### P0-1 · Putting corpus — **start immediately**

The highest-value asset in the project, and it needs nothing that doesn't already exist.

Record 50+ real putts on Blake's existing 720p120 webcam, raw frames, with hand-labelled ground truth. Must include the adversarial cases that broke V1:

- hand entering the zone to place the ball
- putter head sweeping through after impact
- feet in frame
- shadows and varying light
- a second putt immediately after the first (V1's *"second putt in same session does not register"*)
- multiple speeds, both break directions, different ball colours and surfaces

**Ground truth:** a ramp with fixed release heights gives repeatable known speeds. `mph_putting.txt` from cam-putting-py gives a rough speed→distance sanity table.

**Output:** `corpus/` plus a replay harness that re-scores any algorithm change offline. Every later decision is measured against this.

### P0-2 · GSPro protocol truth harness

A fake launch monitor — no camera, no R10. Borrowed from `gspro-connector`'s `FormLaunchMonitor` pattern: a synthetic shot injector that exercises the whole GSPro path independently.

Questions to answer:

- Does `LaunchMonitorIsReady: true` actually move GSPro's displayed status? **This is the bug Blake noticed — confirm it is fixable from our side.**
- Does GSPro accept a putt with `VLA=0, SpinAxis=0, TotalSpin=0`? What does the ball do on screen?
- What `VLA` is actually correct for a putt — 0, 0.5, 1.0? Compare on-screen roll.
- Is there any per-shot acknowledgement distinguishable from other status traffic? *(V1 says no. Confirm, then design for no confirmation.)*
- Does `DistanceToTarget` update per shot and per hole? Is it usable as a plausibility oracle?
- Is a round in progress required for shots to land?
- Does GSPro reject non-monotonic `ShotNumber`?

**Output:** `docs/GSPRO_OPENCONNECT.md` and `docs/GSPRO_PUTTING.md` — the verified putt payload.

### P0-3 · Calibration bench — the #1 technical risk

Design a printed calibration target. Compute a homography. Then measure against known truth: roll a ball a **known distance at a known speed** (fixed-release-height ramp).

**Measurements:** mm/px accuracy across the ROI (centre vs edges); measured vs known speed error; repeatability across re-calibrations; sensitivity to camera nudge.

**Decision gate:** what error bound do we accept before submitting a putt to GSPro?

### P0-4 · E6 Connect ingress spike

Port V1's `monitors/r10.py` handshake and verify it still works against the **current** Garmin Golf app. V1's code was written against an older version.

**Measurements:** handshake success; shot payload shape; reconnect behaviour; behaviour when the phone sleeps or drops WiFi.

**Also:** design the setup flow that eliminates V1's worst field failure — auto-detect and display the LAN IP in-app, and install the firewall rule from the installer rather than asking the user to run `netsh`.

### P0-5 · Camera comparison under locked exposure

Settles ADR-0004's open question with data rather than impression.

Same putts, same light, three configurations: webcam with Logi+ locked shutter/ISO; webcam on auto (V1's condition); iPhone at high FPS with locked exposure. Then repeat the whole matrix with an added LED panel.

**Measurements:** motion blur extent in pixels; centroid stability on a stationary ball; detection rate through the stroke; derived speed variance across repeated identical putts.

**Decides:** whether the iPhone earns its complexity, and whether adding light beats changing cameras.

### P0-6 · iPhone track *(after the webcam path proves out)*

- **Format enumeration** — 15 lines, resolves lens availability, `minExposureDuration`, `maxISO`, pixel formats and `unsupportedCaptureOutputClasses` in one run.
- **`iproxy` → `NWListener` round-trip** over USB — verify the wired path reaches a third-party app's listening socket, and measure throughput.
- **Sustained capture + thermal** — 20-minute run; delivered FPS, `DroppedFrameReason` histogram, `systemPressureState` timeline.
- **Codec impact on centroid** — same putt as raw luma and as H.264; compare derived speed and HLA.
- **LiDAR arming spike** — can a depth frame reliably separate ball-at-mat-level from hand/putter above it?

### Phase 0 exit criteria

- A putting corpus with ground truth, and a replay harness that scores algorithms against it.
- A verified GSPro putt payload that GSPro visibly accepts, with the readiness indicator working.
- A calibration method with a **measured error bound**.
- E6 Connect ingress proven against the current Garmin Golf app.
- A camera decision backed by measurements.

Not in Phase 0: the desktop UI, replay, YOLO, the C# port. None are on the critical path.

---

## Open items

| Item | Owner | Notes |
|---|---|---|
| Code-signing certificate (OV vs EV) | Blake | Needed before first paid release, not before Phase 0 |
| Apple Developer Program ($99) | Blake | Deferred by decision; ADR-0004 makes this safe |
| Licensing / activation mechanism | undecided | Affects install layout — decide before Phase 1 packaging |
| Self-update mechanism (Velopack?) | undecided | Same — decide before Phase 1 packaging |
| Calibration target design | Phase 0 | P0-3 |
| Accepted error bound for putt submission | Blake + data | Output of P0-3 |

---

## Next step

Write the Phase 0 harness specs in enough detail to hand to Claude Code, starting with **P0-1 (corpus)** and **P0-2 (GSPro truth harness)** — the two that need nothing that doesn't already exist and that unblock everything else.
