# GolfSimVision — Project Kickoff

> **Purpose:** This document is the working project brief for Claude Code / Claude Co-Work.  
> Treat the project as a clean-slate implementation. Validate technical assumptions before committing to architecture, and build the system incrementally by phase.

---

## 1. Project Summary

**GolfSimVision** is a golf simulator companion system centered on two capabilities:

1. **Webcam Putting** — use an iPhone as a high-speed external camera source so a Windows PC can detect and measure putts and send putting data to GSPro.
2. **Shot Vision Feedback** — capture a high-frame-rate video window around a full golf shot and provide an immediate replay of the clubhead/impact area.

The initial target user is a **Garmin Approach R10 + GSPro OpenAPI** user.

The iPhone should primarily act as a **high-performance camera/sensor device**. The Windows desktop application should perform the heavier computer-vision processing, coordinate launch-monitor/GSPro state, display shot data and video, and send the final shot/putt information to GSPro.

---

## 2. Primary Product Goal

Build an **ultra-low-latency, high-frame-rate iPhone camera system** that can stream video to a Windows PC over either:

- local Wi-Fi, or
- a direct wired/network connection where technically feasible.

The desktop application will ingest the camera feed and use computer vision to:

- detect a golf ball;
- determine when a ball is ready;
- detect the beginning of a putt;
- track ball movement;
- estimate putting speed and direction;
- coordinate putting/full-shot operating modes;
- maintain a rolling video buffer;
- generate immediate post-shot impact replay;
- integrate the resulting data with GSPro.

The project should prioritize **reliable measurement and low latency over visual image quality**.

---

## 3. Initial Scope

### 3.1 Supported Simulator / Launch Monitor Configuration

For the first implementation:

- **GSPro** is the simulator target.
- Use the **GSPro OpenAPI / Open Connect workflow**.
- Support **Garmin Approach R10** users first.
- Do not design the MVP around other launch-monitor connectors.
- Architect connector interfaces so additional launch monitors can be added later.

### 3.2 Platforms

#### iPhone
Native iOS application responsible for camera capture, camera controls, optional depth/LiDAR experimentation, connection discovery, and streaming.

#### Windows PC
Desktop application responsible for video ingestion, computer vision, replay, launch-monitor/GSPro coordination, UI/status, and GSPro communication.

---

## 4. System Responsibilities

### 4.1 iPhone Application

The iPhone application should:

- initialize and control the rear camera;
- discover the highest useful camera FPS/resolution combinations;
- expose manual camera controls;
- provide a live alignment preview;
- stream camera frames to the PC;
- support Wi-Fi and investigate a practical wired transport;
- expose connection/status information;
- indicate when the camera/streaming system is ready;
- optionally expose LiDAR/depth data if testing proves useful.

Required camera controls should include:

- FPS;
- resolution;
- shutter/exposure duration;
- ISO;
- exposure;
- brightness/contrast or equivalent image-processing controls where appropriate.

The system should favor short exposure times to minimize motion blur.

### 4.2 Windows Desktop Application

The desktop application acts as the central coordinator.

It should:

- connect to the iPhone video source;
- show the live camera feed;
- display camera/stream status;
- receive or coordinate Garmin R10 / GSPro data;
- show GSPro connection state;
- show launch-monitor readiness;
- automatically determine whether the current simulator situation requires putting mode or full-shot mode when possible;
- allow manual shot-mode override;
- display shot/putt data in a side panel;
- run the computer-vision pipeline;
- maintain the replay buffer;
- display automatic post-shot replay;
- provide play/pause/rewind/scrub controls;
- reconnect to GSPro if the connection is lost;
- investigate whether a safe/reliable launch-monitor wake/reconnect command is possible.

---

## 5. Core User Workflow

### Startup

1. User launches **GolfSimVision Desktop**.
2. User launches **GolfSimVision iPhone**.
3. iPhone and PC establish a video connection.
4. Desktop displays a live camera preview.
5. User launches **GSPro** in OpenAPI mode.
6. Garmin R10 connects through the supported OpenAPI workflow.
7. GolfSimVision displays clear connection/readiness indicators for:
   - iPhone camera;
   - video stream;
   - GSPro;
   - launch monitor;
   - shot/putt readiness.

### During Play

1. GolfSimVision determines whether the current shot should use **Full Shot** or **Putting** mode when possible.
2. User can manually override the mode, especially for fringe situations where the golfer chooses to putt rather than chip.
3. In full-shot mode:
   - launch-monitor data remains authoritative for ball-flight data;
   - the camera maintains a rolling high-FPS buffer;
   - impact triggers a short replay.
4. In putting mode:
   - computer vision detects the stationary ball;
   - system indicates **Ready**;
   - computer vision detects ball movement;
   - system tracks the putt;
   - putting metrics are calculated;
   - putting data is sent to GSPro;
   - a short replay may also be shown.

---

## 6. Operating Modes

### 6.1 Putting Mode — Primary MVP

This is the first major computer-vision feature.

The system should attempt to determine:

- ball position;
- ball-ready/stationary state;
- initial movement;
- direction vector;
- initial speed;
- confidence/validity of the detected putt.

Potential later metrics may include:

- launch direction;
- skid/roll transition;
- face/path information if the clubhead can be tracked reliably.

**Do not expand the MVP into full launch-monitor ball-flight measurement.** The initial vision system is primarily a putting measurement system.

### 6.2 Full Shot / Swing Replay Mode

For normal golf shots, the camera is primarily a feedback camera.

Required behavior:

- continuously maintain a short rolling video buffer;
- detect or receive a shot trigger;
- preserve frames immediately before and after impact;
- automatically replay the impact window;
- allow play/pause/rewind/scrubbing.

The purpose is to let the golfer quickly see the clubhead/impact area after each shot.

---

## 7. Computer Vision Pipeline

The PC should own the main CV pipeline unless benchmarking demonstrates a strong reason to move a specific operation onto the phone.

Suggested pipeline:

```text
Camera Frame
    ↓
ROI / Calibration
    ↓
Ball Candidate Detection
    ↓
Stationary Ball Confirmation
    ↓
Ready State
    ↓
Motion / Shot Trigger
    ↓
Ball Tracking Across Frames
    ↓
Pixel-Space Trajectory
    ↓
Calibration Transform
    ↓
Real-World Direction + Speed
    ↓
Confidence / Validation
    ↓
GSPro Shot/Putt Payload
```

Implementation should begin with deterministic/simple CV before introducing ML.

Candidate technologies to evaluate:

- OpenCV;
- background subtraction;
- thresholding;
- contour/blob detection;
- optical flow;
- Hough circle detection;
- Kalman filtering;
- lightweight object detection only if conventional CV is insufficient.

---

## 8. Calibration Requirements

Accurate putting measurement requires converting pixel motion into real-world motion.

The project needs an explicit calibration strategy.

At minimum investigate:

- fixed camera mounting;
- known camera height and angle;
- defined putting ROI;
- one or more known physical reference dimensions;
- perspective correction / homography;
- pixel-to-distance conversion;
- lens distortion correction;
- camera intrinsic calibration if required.

The first prototype should favor a **fixed, repeatable camera position** over attempting arbitrary-camera-position support.

---

## 9. Camera Requirements

### Target

The aspirational target is:

- **up to 240 FPS** where the selected iPhone/camera format actually supports it;
- approximately **720p** at the highest frame-rate mode;
- short shutter duration for low motion blur;
- grayscale/luminance-oriented processing where beneficial.

### Important Engineering Rule

Do **not** assume 240 FPS, a particular resolution, LiDAR synchronization, or a particular pixel format is available on every supported iPhone.

At runtime:

1. enumerate `AVCaptureDevice` formats;
2. enumerate supported frame-rate ranges;
3. determine supported dimensions;
4. choose the best valid configuration;
5. expose the selected mode to the user;
6. gracefully fall back to lower FPS/resolution.

### Monochrome / Luminance

The project may use a YUV format and process primarily the luminance (`Y`) plane for computer vision.

Treat this as a **processing optimization**, not as an assumption that the physical iPhone sensor is natively monochrome.

---

## 10. LiDAR / Depth

LiDAR is an experimental enhancement, **not an MVP dependency**.

Potential uses to investigate:

- estimating camera-to-floor distance;
- simplifying physical calibration;
- determining scene geometry;
- helping isolate the putting surface;
- improving scale/depth estimation.

Possible Apple APIs:

- AVFoundation depth capture;
- ARKit `sceneDepth`.

### Decision Gate

Before integrating LiDAR deeply, build a benchmark/proof-of-concept answering:

1. Is depth data available at a useful rate relative to high-speed video?
2. Can it be synchronized adequately?
3. Does it materially improve putting measurement or calibration?
4. Is the benefit worth the complexity?

If not, continue with calibrated 2D computer vision.

---

## 11. Video Transport

The transport must be benchmarked rather than selected only from theoretical latency estimates.

Candidate approaches include:

- RTSP/H.264;
- SRT;
- another low-latency local-network transport if it provides better frame delivery and easier implementation.

Benchmark each serious candidate for:

- glass-to-PC latency;
- delivered FPS;
- dropped frames;
- frame ordering;
- encoder delay;
- CPU/GPU usage;
- Wi-Fi stability;
- wired stability;
- recovery after connection loss;
- compatibility with the desktop CV pipeline.

### Key Architecture Question

Determine whether the CV pipeline truly needs every frame at the phone's capture FPS.

If encoded streaming prevents reliable delivery at the desired FPS, investigate:

- lower resolution;
- lower FPS;
- hardware encoding settings;
- a custom frame transport;
- USB/network tethering;
- performing trigger detection on-device while sending only required high-speed frame windows.

---

## 12. Connection Modes

### Wi-Fi

Target workflow:

- iPhone and PC are on the same local network;
- application discovers or displays the iPhone endpoint;
- desktop connects without requiring complicated manual setup.

Prefer local discovery such as Bonjour/mDNS if appropriate.

### Wired

A wired mode is desirable for lower latency and stability.

However, **do not assume that plugging an iPhone into Windows automatically creates a generic IP transport suitable for the application**.

Investigate and document supported options, such as:

- iPhone USB tethering / Personal Hotspot networking;
- USB-C Ethernet adapter;
- other Apple-supported local networking paths.

The application should detect the active network interface and advertise the correct endpoint when possible.

---

## 13. GSPro Integration

The desktop application must integrate with GSPro through its supported OpenAPI/Open Connect mechanism.

Required behaviors:

- detect connection state;
- show connected/disconnected status;
- reconnect after loss;
- send valid putting data;
- coordinate shot readiness;
- distinguish full-shot vs putting workflow when the API/data allows it;
- allow manual mode override.

### Important Validation Task

Before implementation, document the exact GSPro OpenAPI message schema and determine:

- what data GolfSimVision receives from GSPro;
- what data GolfSimVision sends to GSPro;
- whether current shot type/club/putting state is actually exposed;
- how shot readiness is represented;
- how reconnect behavior works;
- whether GolfSimVision can coexist with the Garmin R10 connector architecture as envisioned.

Do not build mode-selection logic around an API field until that field is verified.

---

## 14. Garmin R10 Integration

For MVP, Garmin R10 support is required.

The desired user experience is that GolfSimVision can operate as the coordinating application between the R10/OpenAPI workflow and GSPro while also supplying vision-based putting data.

Before coding the connector, validate the actual integration path and ownership of the GSPro connection.

Questions to resolve:

- Does GolfSimVision connect directly to the R10?
- Does an existing R10 connector provide data to GolfSimVision?
- Does GolfSimVision proxy launch-monitor data to GSPro?
- Can multiple clients communicate with the relevant GSPro/OpenAPI components?
- How is readiness communicated?
- Is a software "wake" command for the R10 possible and appropriate?

Treat these as architecture questions, not settled assumptions.

---

## 15. Desktop UI Requirements

A design mock-up already exists separately.

The desktop UI should eventually contain:

- live camera preview;
- camera connection status;
- GSPro connection status;
- launch-monitor status;
- Ready / Not Ready indicator;
- current operating mode;
- manual **Putting / Full Shot** override;
- shot/putt data side panel;
- camera settings;
- replay controls;
- reconnect controls;
- diagnostics/status log.

Suggested status colors:

- **Green:** connected / ready;
- **Red:** disconnected / not ready;
- **Amber:** connecting / calibrating / uncertain.

---

## 16. iPhone UI Requirements

The iPhone UI should remain focused and utility-oriented.

Suggested screens/controls:

- live alignment preview;
- Start/Stop Stream;
- connection mode/status;
- endpoint/device discovery information;
- selected resolution;
- selected FPS;
- shutter speed;
- ISO;
- exposure;
- optional image-processing controls;
- Ready/Streaming indicator;
- diagnostics showing actual measured FPS and dropped frames.

---

## 17. Replay System

Use a rolling in-memory frame/video buffer.

Suggested behavior:

1. continuously buffer the most recent video;
2. trigger on detected shot/putt or external launch-monitor event;
3. retain a configurable pre-trigger window;
4. retain a configurable post-trigger window;
5. present the clip immediately;
6. allow manual playback controls.

Initial target window can be tuned during testing rather than hard-coded as a product requirement.

---

## 18. Non-Functional Requirements

### Performance

- minimize camera-to-PC latency;
- preserve accurate timestamps;
- minimize dropped frames;
- avoid blocking the capture thread;
- keep CV processing real-time;
- expose actual FPS rather than only requested FPS.

### Reliability

- recover cleanly from iPhone disconnect;
- recover from GSPro disconnect;
- recover from stream interruption;
- avoid duplicate shot submissions;
- reject low-confidence false putts.

### Diagnostics

Log at minimum:

- camera model;
- active format;
- requested FPS;
- measured FPS;
- shutter;
- ISO;
- stream bitrate;
- dropped frames;
- network latency where measurable;
- CV processing time;
- GSPro connection events;
- shot trigger timestamps;
- calculated putting metrics;
- confidence;
- errors.

---

## 19. Recommended Architecture

```text
┌─────────────────────────────┐
│          iPhone App         │
│ AVFoundation Camera Capture │
│ Manual Camera Controls      │
│ Optional Depth / LiDAR      │
│ Stream / Transport Layer    │
└──────────────┬──────────────┘
               │
        Wi-Fi / Wired IP
               │
               ▼
┌─────────────────────────────┐
│      Windows Desktop App    │
│                             │
│ Video Receiver              │
│ Frame Timestamping/Buffer   │
│ Calibration                 │
│ Computer Vision             │
│ Putting Measurement         │
│ Swing Replay                │
│ UI / Diagnostics            │
│ Connector Orchestration     │
└──────────┬───────────┬──────┘
           │           │
           │           └──────── Launch Monitor / R10 Path
           │
           ▼
┌─────────────────────────────┐
│            GSPro            │
│       OpenAPI / Connect     │
└─────────────────────────────┘
```

Keep these layers modular:

```text
Camera Capture
Transport
Video Receiver
Frame Buffer
Calibration
Ball Detection
Ball Tracking
Putting Metrics
Replay
GSPro Connector
Launch Monitor Connector
Desktop UI
Diagnostics
```

---

## 20. Suggested Repository Structure

```text
GolfSimVision/
├── README.md
├── docs/
│   ├── PROJECT_BRIEF.md
│   ├── ARCHITECTURE.md
│   ├── GSPro_OPENAPI.md
│   ├── CAMERA_CAPABILITIES.md
│   ├── TRANSPORT_BENCHMARKS.md
│   ├── CALIBRATION.md
│   └── TEST_PLAN.md
├── ios/
│   └── GolfSimVisionCamera/
├── desktop/
│   └── GolfSimVision/
├── shared/
│   └── protocol/
├── prototypes/
│   ├── camera-capture/
│   ├── transport/
│   ├── ball-tracking/
│   └── gspro-openapi/
└── tests/
```

---

## 21. Development Phases

### Phase 0 — Technical Validation

**Goal:** prove the core assumptions before building the product UI.

Tasks:

- verify GSPro OpenAPI behavior and message schema;
- verify the Garmin R10 integration path;
- enumerate real iPhone camera modes;
- prove sustained high-FPS capture;
- benchmark luminance/grayscale processing;
- benchmark Wi-Fi transport;
- benchmark wired transport;
- measure end-to-end latency and delivered FPS;
- determine whether LiDAR is useful;
- create a simple PC receiver;
- record representative putting footage.

**Exit criteria:**

- known camera configuration that works;
- known transport approach;
- measured latency/FPS;
- verified GSPro communication path;
- documented R10 architecture;
- sample video available for CV development.

### Phase 1 — Camera Streaming MVP

Build the iPhone camera application and PC receiver.

Deliverables:

- iPhone live preview;
- manual FPS/resolution/exposure/ISO controls;
- stream start/stop;
- Wi-Fi connectivity;
- wired connectivity if Phase 0 validates it;
- PC live preview;
- measured FPS/dropped-frame diagnostics;
- reconnect handling.

**Exit criteria:** PC receives a stable, timestamped video feed suitable for CV testing.

### Phase 2 — Webcam Putting MVP

Deliverables:

- camera calibration;
- putting ROI;
- stationary ball detection;
- Ready state;
- movement trigger;
- ball tracking;
- speed calculation;
- direction calculation;
- confidence validation;
- GSPro putting submission;
- manual mode override.

**Exit criteria:** repeated real putts produce acceptably accurate speed/direction and are reliably submitted to GSPro without false shots.

### Phase 3 — Shot Vision Replay

Deliverables:

- rolling buffer;
- shot trigger;
- pre/post-impact clip extraction;
- automatic replay;
- play/pause/rewind/scrub;
- configurable replay timing.

### Phase 4 — Integrated Desktop Experience

Deliverables:

- polished desktop dashboard;
- launch-monitor status;
- GSPro status;
- camera status;
- shot data panel;
- reconnect actions;
- settings persistence;
- diagnostics;
- mode automation where API support is verified.

### Phase 5 — Advanced Vision / Sensor Features

Only after the core product works:

- LiDAR-assisted calibration;
- clubhead tracking;
- impact-point analysis;
- improved ML-based detection;
- additional launch monitors;
- alternate cameras;
- external global-shutter camera support.

---

## 22. Testing Strategy

### Camera Tests

Test across supported devices:

- actual sustained FPS;
- exposure limits;
- resolution;
- thermal throttling;
- dropped frames;
- low-light behavior;
- motion blur.

### Network Tests

Test:

- 5 GHz/6 GHz Wi-Fi where available;
- congested Wi-Fi;
- wired/tethered path;
- reconnect;
- packet loss;
- latency;
- long-running stability.

### Putting Tests

Create a controlled test matrix:

- multiple known distances;
- multiple speeds;
- straight putts;
- left/right start directions;
- different golf-ball colors/markings;
- different putting surfaces;
- lighting variations;
- shadows;
- clubhead entering the ROI;
- golfer feet entering the ROI.

Compare GolfSimVision measurements against a trusted reference method whenever possible.

---

## 23. Definition of MVP Success

The MVP is successful when a Garmin R10 + GSPro user can:

1. launch GolfSimVision;
2. connect an iPhone camera;
3. see a stable high-FPS live preview;
4. calibrate a fixed putting area;
5. receive a clear Ready indication;
6. hit a putt;
7. have GolfSimVision detect and measure it;
8. have the putt accepted by GSPro with useful speed/direction accuracy;
9. continue playing without manually switching applications for each shot;
10. receive stable connection/readiness feedback.

Shot replay is the next major feature after putting measurement is reliable.

---

## 24. Open Questions / Decisions Required

Do not silently decide these. Research/prototype them and document the result.

- What Windows desktop technology should be used?
- What is the exact GSPro OpenAPI architecture for the R10 use case?
- Does GolfSimVision proxy R10 data or coexist with another connector?
- What GSPro state can actually be read?
- What camera FPS/resolution combinations are available on target iPhones?
- What is the real sustained encoded-stream FPS?
- Is RTSP, SRT, or another transport best?
- What is the best wired iPhone-to-Windows networking method?
- Is LiDAR useful enough to keep?
- What camera position gives the best putting accuracy?
- What calibration target/process should be used?
- What accuracy threshold is required before submitting a putt?
- How should duplicate/false shots be suppressed?
- Can the R10 be programmatically awakened/reconnected?
- How should timestamps be synchronized between phone, PC, launch monitor, and GSPro?

---

## 25. Engineering Principles for Claude Code

When developing this project:

1. **Validate before assuming.** Hardware/API behavior must be verified.
2. **Prototype risky components independently.** Camera FPS, transport, GSPro, and CV should each have small test harnesses.
3. **Do not begin with the polished UI.** Prove the data path first.
4. **Keep modules replaceable.** Transport, CV algorithms, and launch-monitor connectors will evolve.
5. **Instrument everything.** Performance data is essential.
6. **Prefer deterministic CV first.** Add ML only when needed.
7. **Do not make LiDAR a dependency.**
8. **Do not hard-code 240 FPS.** Negotiate supported formats.
9. **Preserve timestamps.** Accurate timing is fundamental to speed calculations.
10. **Keep GSPro submissions isolated behind a connector interface.**
11. **Prevent duplicate shots.** Use an explicit shot-state machine.
12. **Build one vertical slice at a time.**

---

## 26. Suggested First Claude Code Task

Start with **Phase 0 only**.

Create the initial repository and technical-validation harnesses. Do not attempt the complete application yet.

### First implementation objectives

1. Create the repository structure.
2. Add an iOS AVFoundation capability inspector that reports:
   - device/camera name;
   - format dimensions;
   - supported FPS ranges;
   - pixel formats;
   - min/max exposure duration;
   - min/max ISO;
   - depth-capable formats where available.
3. Add a minimal iOS high-FPS capture prototype that:
   - selects a requested FPS only if supported;
   - timestamps frames;
   - counts actual delivered FPS;
   - reports dropped frames;
   - displays a preview.
4. Add a minimal Windows/Python or selected desktop-stack receiver prototype for transport testing.
5. Create a transport benchmark plan.
6. Create a GSPro OpenAPI investigation document before implementing connector assumptions.
7. Create an R10 integration investigation document.
8. Keep experimental code under `/prototypes` until the architecture is validated.

### Required output from this task

At completion, provide:

- what was implemented;
- how to run each prototype;
- measured/observable results;
- assumptions that were confirmed;
- assumptions that were disproven;
- unresolved blockers;
- recommendation for the next task.

---

## 27. Source Notes and Prototype Code

The original project write-up contains exploratory notes, external references, and prototype code. These are **research material only** and should not be treated as production-ready or technically verified.

In particular, verify before reuse:

- HaishinKit APIs and whether the shown local RTSP/RTMP server approach is valid for the chosen version;
- iOS high-FPS format availability;
- grayscale/Y-plane assumptions;
- bandwidth claims;
- USB networking assumptions;
- LiDAR synchronization/rate;
- GSPro OpenAPI behavior;
- Garmin R10 connection ownership.

See the companion source file or appendix for the original exploratory snippets.


---

## 28. Existing V1 Application — Visual Reference Only

An existing V1 version of GolfSimVision may be provided for reference. **V1 is not the architectural foundation for this project. This is a clean-slate implementation.**

Use V1 primarily to preserve the desktop application's visual design, look, feel, layout, visual hierarchy, panels, navigation, colors, typography, spacing, status indicators, camera/video presentation, shot-data presentation, and overall GolfSimVision identity.

Do not begin by extending or repairing V1, and do not inherit its frameworks, networking, camera handling, GSPro/R10 integration, state management, dependencies, concurrency model, or repository structure simply because they exist.

The governing question is: **What is the best architecture for GolfSimVision if we were building it correctly from scratch today?**

There may be isolated useful assets or code. Classify them as **DESIGN REFERENCE**, **POTENTIAL REUSE**, or **DO NOT REUSE**. When uncertain, favor a clean implementation.

Early in planning, create `docs/UI_DESIGN.md` from V1 documenting screenshots/reference views, colors, typography, spacing, dimensions, components, icons/assets, layouts, and important interaction behavior. The new project should then be able to reproduce the V1 visual language without depending on the old codebase.

> **Preserve the V1 design. Reconsider the V1 engineering.**

---

## 29. OpenFlight Reference Project — Required Research

Study the open-source **OpenFlight DIY Launch Monitor** as an external engineering reference:

- Repository: `https://github.com/open-flight/openflight`
- Camera/YOLO: `https://github.com/open-flight/openflight/blob/main/docs/development/camera-yolo.md`
- GSPro: `https://github.com/open-flight/openflight/blob/main/docs/simulator/gspro.md`
- Simulator connectors: `https://github.com/open-flight/openflight/blob/main/docs/simulator/README.md`

OpenFlight is research/reference material, not GolfSimVision's architecture.

### Camera / Vision

Investigate relevant source, models, scripts, docs, and history for ball/club detection, YOLO/ONNX, OpenCV, high-FPS capture, OV9281/global shutter, capture-vs-inference threading, buffering, latency, ROI processing, ball-ready/shot detection, calibration, and speed/direction measurement.

Create `docs/research/OPENFLIGHT_ANALYSIS.md`. Classify findings as **DIRECTLY RELEVANT**, **USEFUL REFERENCE**, **EXPERIMENT WORTH REPRODUCING**, **NOT APPLICABLE**, or **WARNING / LESSON LEARNED**.

### GSPro / OpenConnect

Study OpenFlight's actual GSPro/OpenConnect implementation, including TCP port 921 lifecycle, JSON codec, shot submission/acknowledgement, errors, reconnect/backoff, connection status, player/club updates, shot numbering, duplicate prevention, and simulator-neutral connector abstractions.

Pay particular attention to GSPro player update **code 201** and selected club. OpenFlight treats `PT`/putter as out of scope; GolfSimVision should investigate whether it can be one signal for automatic **Webcam Putting Mode**, while retaining manual override and handling fringe/chip/multiplayer/reconnect edge cases.

Create `docs/research/OPENFLIGHT_GSPRO_ANALYSIS.md`.

### GSPro Putting

Create `docs/research/GSPRO_PUTTING.md` documenting exactly what GSPro expects for putting through OpenConnect V1: required BallData fields, speed and HLA/VLA representation, spin/carry behavior, ClubData requirements, low-speed behavior, acknowledgements, and errors.

The CV layer should output a protocol-neutral `PuttMeasurement` (timestamp, ball speed, horizontal launch angle/direction, confidence). A separate GSPro connector should translate it to OpenConnect JSON.

### Architecture Question

Evaluate, but do not assume, a modular flow where both Garmin R10 full shots and iPhone vision-based putts become a common internal shot model and a simulator connector owns the GSPro/OpenConnect connection.

One of Phase 0's most important questions is:

> **Should GolfSimVision own the single GSPro/OpenConnect connection and route both Garmin R10 full-shot data and GolfSimVision vision-based putting data?**

Every adopted OpenFlight idea must pass:

**ASSUMPTION → EXPERIMENT → MEASUREMENT → DECISION**
