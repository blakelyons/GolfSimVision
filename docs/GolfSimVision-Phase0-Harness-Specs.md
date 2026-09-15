# GolfSimVision — Phase 0 Harness Specs (P0-1, P0-2)

**Date:** 2026-09-15
**Status:** Ready to hand to Claude Code.
**Companions:** `GolfSimVision-Architecture-Decisions.md`, `GolfSimVision-Project-Readiness-Review.md`

Both harnesses are **Python**, both live under `/prototypes`, and **none of this code ships**. It exists to answer questions and produce data. The settled pipeline gets ported to C# later (ADR-0006). The one exception is noted in P0-2 §2 — the protocol layer is written to be ported.

These two run independently and can be worked in either order or in parallel. P0-1 needs the webcam and the putting mat. P0-2 needs GSPro and nothing else.

---

# P0-1 · Putting Corpus & Replay Harness

## Purpose

Produce the dataset every later decision is scored against, and measure two things nobody in the prior art measured: **frame-interval jitter** (which sets the floor on speed accuracy) and **detection rate against adversarial cases** (which is what actually broke V1 in the field).

This is not a detector. It records, it labels, and it replays. Detection algorithms come later and are scored against what this produces.

## Deliverables

```
prototypes/corpus/
├── README.md
├── capture.py            # record clips from webcam
├── label.py              # tier-2 per-frame ball labelling
├── replay.py             # replay harness — feeds clips to any detector
├── stats.py              # corpus summary + jitter report
└── corpus/               # output (gitignored; see size note)
    ├── index.jsonl
    └── clip_0001/
        ├── clip.json
        └── frames/
            ├── 000000.png
            └── ...
```

## 1. Capture tool — `capture.py`

**Runs on:** the Windows PC, with the webcam connected and positioned over the putting mat exactly as it will be in use.

### Camera opening — the part that is easy to get wrong

```python
cap = cv2.VideoCapture(index, cv2.CAP_DSHOW)
cap.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc(*'MJPG'))   # MUST be first
cap.set(cv2.CAP_PROP_FRAME_WIDTH,  1280)
cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 720)
cap.set(cv2.CAP_PROP_FPS, 120)
```

Three rules, each of which has bitten someone:

1. **FOURCC must be set before width/height/fps.** Set it after and it is silently ignored.
2. **MJPG is almost certainly mandatory at 120 fps.** Uncompressed YUY2 at 720p120 is 221 MB/s, far beyond USB 2.0's ~40 MB/s practical throughput. Most webcams only expose their high-frame-rate modes over MJPG. This means the corpus is *already* intra-frame compressed at source — acceptable (intra-only, no inter-frame smearing), but it must be recorded as a known property of the data, not discovered later.
3. **Never trust the requested settings.** Read back `CAP_PROP_FRAME_WIDTH/HEIGHT/FPS/FOURCC` after opening and log both requested and actual. A camera that silently gives you 30 fps will produce a corpus that quietly invalidates everything.

Also probe and log, once at startup: `CAP_PROP_EXPOSURE`, `CAP_PROP_AUTO_EXPOSURE`, `CAP_PROP_GAIN`, `CAP_PROP_BRIGHTNESS`, `CAP_PROP_CONTRAST`. These go in every clip's metadata. If Logi+ is being used to lock shutter and ISO, that fact and the values go in the clip metadata by hand (`--notes`), because DirectShow will not reliably report them.

### Capture loop

Single dedicated capture thread. Nothing but read → timestamp → push to ring buffer. No processing, no encoding, no display work on this thread.

```python
ok, frame = cap.read()
t = time.perf_counter()        # immediately after read returns
ring.append((t, frame))
```

`time.perf_counter()` is the monotonic high-resolution clock. Take it **immediately after `cap.read()` returns**, before any conversion. V1's mistake was timestamping at the top of a processing loop, which folds the previous iteration's processing time into the interval.

Convert to grayscale on the capture thread only if it measurably helps — otherwise store what arrives and convert at save time.

Ring buffer: `collections.deque(maxlen=N)` where N = `pre_roll_seconds × fps`. Default pre-roll **4 seconds** (480 frames at 120 fps), configurable.

A separate display thread shows a live preview at ~15 fps, downscaled, with a running readout: actual FPS (rolling 1 s), ring fill level, last-clip filename. **The preview must never gate or block capture.** (V1 coupled detection to whether a browser was connected; do not recreate that in any form.)

### Triggering

**Manual is primary.** Press `SPACE` *after* the putt — the ring buffer already holds it. This is the most reliable way to collect data: no auto-trigger tuning, nothing missed, and adversarial clips (hand entering the zone, putter sweep with no putt) can be captured deliberately.

Keys:

| Key | Action |
|---|---|
| `SPACE` | Save the ring buffer as a clip |
| `V` | Mark the next save as a **valid putt** |
| `A` | Mark the next save as **adversarial / non-putt** |
| `R` | Discard the last saved clip |
| `Q` | Quit |

A second mode, `--auto-trigger`, runs simple ROI frame-differencing and saves on motion. **This exists to characterise a naive trigger's false-positive rate, not to collect the corpus.** Run it for a session while deliberately placing balls by hand and note how often it fires wrongly. That number is a baseline for the real arming logic to beat.

### Clip storage

On save: dump the ring buffer as **8-bit grayscale PNG**, one file per frame, zero-padded index, into `clip_NNNN/frames/`.

PNG chosen for lossless storage, universal tooling, and individually inspectable frames. Encoding happens after the fact from the ring buffer, so it does not need to keep up with capture.

**Size:** ~300–500 KB per grayscale 720p PNG. A 4-second clip at 120 fps ≈ 480 frames ≈ **190 MB**. A 50-clip corpus ≈ **9–10 GB**. Plan storage accordingly. `--roi x,y,w,h` crops at save time and cuts this 3–4×; use full frame for at least the first 10 clips so ROI choices can be revisited later, then crop if size becomes a problem.

### Clip metadata — `clip.json`

```json
{
  "clip_id": "clip_0001",
  "created_utc": "2026-09-15T19:42:11Z",
  "kind": "valid_putt",
  "camera": {
    "source": "usb_webcam",
    "device_name": "Logitech Brio",
    "requested": {"width":1280,"height":720,"fps":120,"fourcc":"MJPG"},
    "actual":    {"width":1280,"height":720,"fps":120,"fourcc":"MJPG"},
    "exposure_locked": true,
    "exposure_note": "Logi+ shutter 1/2000, ISO 400",
    "props": {"CAP_PROP_EXPOSURE": -7, "CAP_PROP_GAIN": 128}
  },
  "geometry": {
    "camera_height_in": 31.5,
    "camera_fov_deg": 78.0,
    "mount": "overhead, ball travels left to right"
  },
  "conditions": {
    "lighting": "room ambient, no added light",
    "surface": "practice mat, medium",
    "ball": "white Titleist ProV1"
  },
  "frames": {
    "count": 480,
    "first_index": 0,
    "timestamps_s": [0.0, 0.00834, 0.01661, "..."]
  },
  "timing": {
    "measured_fps": 119.7,
    "interval_ms": {"median": 8.35, "p95": 9.10, "max": 24.6, "jitter_sd": 0.71},
    "gap_count": 2
  },
  "ground_truth": {
    "method": "two_gate_manual",
    "speed_mph": 4.83,
    "hla_deg": -1.2,
    "gate_distance_mm": 400.0,
    "gate_frames": [112, 211],
    "notes": "ramp release height 30cm"
  },
  "labels_tier2": false,
  "notes": ""
}
```

**`timestamps_s` is the single most important field in the file.** It is the measured per-frame arrival time relative to the first frame — never an assumed `1/fps`. Every speed measurement derives from it.

`index.jsonl` at the corpus root carries one flattened line per clip for fast querying.

## 2. Frame-interval jitter — a first-class output

This is a Phase 0 finding in its own right and it belongs in `docs/CAMERA_CAPABILITIES.md`.

`stats.py` reports, across the corpus: median / p95 / max frame interval, standard deviation, and a count of gaps (intervals > 1.5 × median). It renders a histogram.

**Why it matters.** DirectShow and MSMF give no reliable hardware timestamp for a USB webcam, so arrival time is all we have, and arrival time includes USB scheduling and OS jitter. If intervals are 8.3 ms ± 0.7 ms, that is roughly 8% timing noise per interval — which is *fine*, because a least-squares fit over 25–90 points averages it down. But if it is ± 4 ms with frequent gaps, that is a hard floor on speed accuracy and it changes the design.

**Nobody in the prior art measured this.** cam-putting-py timed two samples with Python wall-clock and never characterised the noise — which is very likely a large part of why its numbers were erratic.

## 3. Ground truth

Two methods, used together.

### Primary — two-gate manual timing

Place two physical marks on the mat a **measured** distance apart (400 mm is a good default), both inside the camera's view and inside the region where the ball is rolling freely. Then, in the recorded clip, a human frame-steps to find the frame where the ball centre crosses each mark.

```
speed = gate_distance_mm / (timestamps[gate2] - timestamps[gate1])
```

This is exact for that clip, requires no calibration, and — critically — **decouples "did the algorithm find the ball" from "is the scale right."** When the detector later disagrees, this tells you which half is wrong.

Tedious, so apply it to ~20 clips, not all 50.

### Secondary — ramp release height

A ramp with marked release heights gives repeatable, physically-computable speeds. For a solid sphere rolling without slipping from height *h*:

```
v = sqrt(10 · g · h / 7)
```

| Release height | Speed | |
|---|---|---|
| 10 cm | 1.18 m/s | 2.6 mph |
| 20 cm | 1.67 m/s | 3.7 mph |
| 30 cm | 2.05 m/s | 4.6 mph |
| 45 cm | 2.51 m/s | 5.6 mph |
| 60 cm | 2.90 m/s | 6.5 mph |

Covers the putting range well. Accurate to maybe 5% — ramp friction, initial slip, and the ramp-to-mat transition all lose energy — so it is a **repeatability** tool and a rough absolute, not the primary truth. Its real value is producing many clips at the *same* speed, so measurement variance can be separated from measurement bias.

`label.py` provides the frame-stepper for both: arrow keys to step, click to mark the ball, `G` to mark a gate crossing, writes back into `clip.json`.

## 4. Test matrix

Aim for **50+ clips minimum**. Every row in the adversarial block exists because it broke V1 or cam-putting-py in the field.

### Valid putts (~30 clips)

| Dimension | Values |
|---|---|
| Speed | slow (~2 mph), medium (~5 mph), firm (~9 mph) — 5 each |
| Line | straight, ~3° left, ~3° right |
| Ball | white, yellow, and one with a heavy alignment line/logo |
| Surface | the usual mat, plus one other if available |
| Lighting | room ambient; repeat a subset with an added LED panel |

### Adversarial / non-putt (~20 clips)

| Case | Why | Origin |
|---|---|---|
| Hand reaches in and places the ball, no putt | False trigger | V1 TESTING.md #3, **highest severity** |
| Putter head enters and sweeps through, no ball | False trigger | cam-putting-py `dc5928e` "Putter Interference" |
| Putter follow-through immediately after a real putt | Double-trigger | V1 direction-guard logic |
| **Two putts in quick succession** | Re-arm failure | V1 TESTING.md #4, **highest severity** |
| Ball retrieved and re-placed | Re-arm | V1 #4 |
| Feet / shadow crossing the zone | False positive | V1 DETECTION_PLAN |
| Ball rolls in from off-frame | Invalid start | — |
| Ball stops inside the zone | Incomplete track | — |
| Hard direct sunlight / bright hotspot | White-ball false positive | V1 DETECTION_PLAN |
| Ball partially occluded at the start | Detection | — |

### Also record

- **One 10-minute idle clip** (empty mat, nobody present) as a false-positive soak test.
- **One session in `--auto-trigger` mode** with deliberate hand placement, to baseline naive trigger false-positive rate.

## 5. Replay harness — `replay.py`

The piece that makes the corpus permanently valuable. Feeds clips to any detector implementation and scores it, so every algorithm change is re-scored offline in seconds instead of requiring a trip to the mat.

```python
class Detector(Protocol):
    def reset(self) -> None: ...
    def push_frame(self, t: float, frame: np.ndarray) -> Optional[PuttMeasurement]: ...
```

```bash
python replay.py --detector detectors.hsv_v1 --corpus ./corpus --report out/hsv_v1.json
```

`replay.py` iterates clips, feeds frames **with their recorded timestamps** (not wall-clock, and not assumed constant interval), and collects whatever the detector emits.

### Scorecard

| Metric | Definition |
|---|---|
| **True positives** | valid-putt clips producing exactly one measurement |
| **False negatives** | valid-putt clips producing none |
| **False positives** | adversarial clips producing any measurement ← **the number that matters most** |
| **Double-fires** | any clip producing more than one |
| Speed error | mean, median, p95 absolute % error vs ground truth |
| HLA error | mean, median, p95 absolute degrees |
| Track points | distribution of points per measurement |
| Rejections | count by reason code |

**Under ADR-0007 (precision over recall), false positives are weighted above false negatives.** A detector that measures 80% of putts with zero false positives beats one that measures 95% with three. Report both, but rank on false positives.

Include a baseline: a straight port of cam-putting-py's approach (HSV + two-sample gate + ball-radius scale). Not because we will ship it, but because *"we beat the existing free tool by X"* is the only meaningful benchmark available, and it is a commercially relevant number.

## 6. Exit criteria

- 50+ clips, ≥ 20 adversarial, with metadata and timestamps.
- 20+ clips with two-gate ground truth.
- 15+ clips with tier-2 per-frame ball labels.
- A written frame-interval jitter characterisation → `docs/CAMERA_CAPABILITIES.md`.
- `replay.py` scoring the cam-putting-py baseline end-to-end.

---

# P0-2 · GSPro Protocol Truth Harness

## Purpose

A fake launch monitor. No camera, no R10. It connects to GSPro on 127.0.0.1:921, sends crafted payloads, logs everything in both directions, and settles the protocol questions that two independent open-source implementations left open.

The headline question is Blake's: **does `LaunchMonitorIsReady: true` actually move GSPro's readiness indicator?** Every heartbeat `gspro-r10.exe` sends carries `false`, and its own config marks the feature inert — so the indicator may be perfectly functional and simply never fed. Confirming that turns an observed annoyance into a shipped feature.

## Deliverables

```
prototypes/gspro-harness/
├── README.md
├── protocol.py       # framing, payload builders, response parser  ← written to be ported
├── harness.py        # interactive REPL + scripted scenarios
├── scenarios/        # one file per experiment
└── logs/             # timestamped bidirectional transcripts
```

## 1. Prerequisites

- GSPro licence must be the **Open Connect / OpenAPI** type. An R10-bound licence **disables port 921**. Convert at `gsprogolf.com/convert.html`, then install latest GSPro and clear GSPro Connect settings.
- GSPro Connect window open, showing "Waiting for connection."
- **`gspro-r10.exe` must not be running.** Port 921 is single-client — confirmed. It will hold the socket.
- A round in progress for shot tests.

## 2. Protocol layer — `protocol.py`

This is the one piece written with porting in mind (ADR-0006 moves it to C#), so it gets written properly the first time.

### Framing

OpenConnect has **no delimiter and no length prefix**. Frame on balanced top-level braces, string-and-escape aware, with a resync guard:

```python
def find_json_end(buf: bytes) -> Optional[int]:
    """Index past the first complete top-level JSON object in buf, or None.
    String-aware so that braces inside quoted values don't count."""
```

Requirements, each with a test:

- Two complete objects arriving in **one** `recv()` must yield two parsed messages.
- One object split across **two** `recv()` calls must yield one message once complete.
- A `{` inside a string value must not affect depth. *(This is the bug in the Java implementation — its brace counter is not string-aware.)*
- Buffer exceeding **64 KB** with no complete frame: log a warning, clear, resync.

Outbound: `json.dumps(..., separators=(",",":"))`, UTF-8, **no trailing newline**.

### Outbound payload

```python
{
  "DeviceID": "GolfSimVision",
  "Units": "Yards",
  "ShotNumber": <int>,
  "APIversion": "1",
  "BallData": {"Speed":…, "SpinAxis":…, "TotalSpin":…, "HLA":…, "VLA":…},
  "ClubData": {…},
  "ShotDataOptions": {
      "ContainsBallData": bool, "ContainsClubData": bool,
      "LaunchMonitorIsReady": bool, "LaunchMonitorBallDetected": bool,
      "IsHeartBeat": bool
  }
}
```

Verified against the spec: `APIversion` is the **string** `"1"` with a **lowercase v**; `LaunchMonitorBallDetected` is spelled correctly (the Java project's `LaunchMontiorBallDetected` is that project's own typo). Required `BallData`: `Speed`, `SpinAxis`, `TotalSpin`, `HLA`, `VLA`.

### ShotNumber

Signed 32-bit, seeded from `int(time.time())` — epoch **seconds**. Monotonic across restarts, stays under 2,147,483,647 until 2038. Epoch *milliseconds* overflow and GSPro replies `501 "Bad format"`.

### Inbound

```python
{"Code": int, "Message": str, "Player": dict | None}
```

| Code | Meaning |
|---|---|
| 200 | shot received |
| 201 | player information |
| ≥500 | error (501 = bad format) |
| other | log verbatim — do not discard silently in the harness |

Known-good 201, captured from Blake's own V1 logs:

```json
{"Code":201,"Message":"GSPro Player Information",
 "Player":{"Handed":"RH","Club":"PT","DistanceToTarget":3.3,"Surface":null}}
```

### Logging

Every byte in both directions, with `perf_counter` timestamps, to `logs/session_<ts>.log`, in V1's readable format:

```
14:06:00.688  GSPro >> {"Code":201,...}
14:06:01.855  GSPro << {"DeviceID":"GolfSimVision",...}
```

Plus a parallel `.jsonl` for machine analysis. **These transcripts are the deliverable** — `docs/GSPRO_OPENCONNECT.md` is written from them.

## 3. Experiment matrix

Each scenario is scripted and repeatable. Several need a GSPro screenshot before and after; note which.

| # | Experiment | Procedure | Records |
|---|---|---|---|
| **1** | Baseline chatter | Connect, send nothing, log 60 s | What GSPro sends unprompted, and when |
| **2** | **Readiness = false** | Heartbeat every 5 s, `LaunchMonitorIsReady: false`, 30 s. **Screenshot.** | Indicator state |
| **3** | **Readiness = true** | Flip to `true`, 30 s. **Screenshot.** | **Does the indicator change?** ← the key question |
| **4** | Readiness toggle | Alternate every 10 s for 2 min | Does it track live, or latch? |
| **5** | Club change | Change clubs in GSPro through the full bag | Every 201. Confirm `PT`. Capture the full `Player` object per club |
| **6** | DistanceToTarget | Play a hole normally, tee → green | Does it update per shot? Units? Behaviour on the green? |
| **7** | Full shot | Driver: Speed 150, HLA 0, VLA 12, TotalSpin 2500, SpinAxis 0 | Does it play? Response code? |
| **8** | **Putt, VLA = 0** | Speed 5, HLA 0, VLA 0, TotalSpin 0, SpinAxis 0, `ContainsClubData: false` | Accepted? What does the ball do on screen? |
| **9** | VLA sweep | Same putt, VLA ∈ {0, 0.5, 1.0, 2.0} | Which looks correct on screen? |
| **10** | **TotalSpin sweep** | Same putt, TotalSpin ∈ {0, rolling estimate} — see §4 | Does realistic rolling spin behave better than 0? |
| **11** | HLA sign | HLA = +2 then −2 | **Which way does the ball go?** Trivially easy to ship backwards |
| **12** | Speed range | Putts at 1, 3, 5, 8, 12 mph | Lower/upper bound where GSPro misbehaves |
| **13** | ShotNumber abuse | non-monotonic, 0, negative, 2³¹, 2³¹+1 | Confirm 501 on overflow; find the real rules |
| **14** | APIversion casing | `"APIversion"` then `"APIVersion"` | Both accepted? *(gspro-r10 sends the capital-V form and works, so likely case-insensitive — confirm)* |
| **15** | Ack correlation | 3 shots in rapid succession | How many 200s? Any way to correlate to a shot? |
| **16** | Reconnect | Drop the socket mid-round, reconnect, send a shot | Does GSPro recover? ShotNumber expectations? |
| **17** | No round active | Send a shot from the GSPro menu | Accepted, ignored, or error? |
| **18** | Heartbeat absence | Connect, send nothing for 5 min | Does GSPro time us out? |
| **19** | Malformed input | Missing required BallData field; bad JSON | Error code? Connection dropped or kept? |
| **20** | ContainsBallData false + BallData present | Contradictory payload | Which wins? |

## 4. The rolling-spin question

Both cam-putting-py and V1 hard-code `TotalSpin = 0` for putts. Nobody tested whether that is right.

A putted ball is *rolling*, so it has real topspin. Ball circumference is π × 42.67 mm = 134 mm, which gives a clean rule:

```
rpm ≈ 200 × speed_mph
```

(5 mph → ~1000 rpm; 8 mph → ~1600 rpm.)

So experiment 10 should test at least: `TotalSpin: 0`, and `TotalSpin: 200 × mph` — and, if GSPro's response suggests it distinguishes them, the `BackSpin` sign convention for topspin, since forward roll is negative backspin in launch-monitor terms.

If GSPro's putting physics ignores spin entirely then 0 is fine and this is settled cheaply. If it does not, this is a free accuracy improvement nobody else has made.

## 5. Interactive mode

`harness.py` with no scenario drops into a REPL, for exploring GSPro's behaviour without editing code:

```
> connect
> heartbeat ready=true
> putt speed=5.0 hla=-1.5 vla=0 spin=0
> shot speed=150 hla=0 vla=12 spin=2500
> raw {"DeviceID":"GolfSimVision",...}
> watch 30
> status
```

`watch N` logs inbound traffic for N seconds without sending — useful while changing clubs or playing a hole in GSPro.

## 6. Exit criteria

- All 20 experiments run, with transcripts.
- **Experiment 3 answered** — readiness indicator either works when fed `true`, or does not, with evidence either way.
- A verified putt payload that GSPro accepts and renders correctly → `docs/GSPRO_PUTTING.md`.
- HLA sign convention confirmed and written down.
- ShotNumber rules confirmed empirically.
- `protocol.py` passing its framing tests, ready to port to C#.
- `docs/GSPRO_OPENCONNECT.md` written from observed traffic, superseding all inference.

---

## Suggested order of work

P0-2 is the smaller job and needs only GSPro, so it can run in an evening. P0-1 is a bigger build plus several sessions on the mat.

They are independent — start whichever suits, or run them in parallel. Everything downstream waits on the corpus, so if only one gets started, start P0-1.

Explicitly **not** in scope for either: any detector implementation beyond the cam-putting-py baseline, any UI, any C#, any iPhone work.
