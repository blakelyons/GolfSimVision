# Getting a Logitech Brio to a true 1280x720 @ 60 fps video feed (Windows)

Hand this to the GolfSimVision project. It documents what was learned fixing the legacy cam-putting-py / ball_tracking app. Result in that app: **60 fps confirmed**, Brio at MJPEG 1280x720 @ 60.

## 1. The short version

1. **OpenCV cannot select MJPEG on this Brio.** With the DirectShow (`CAP_DSHOW`) and Media Foundation (`CAP_MSMF`) backends, on OpenCV 4.7.0 and 4.10, in every call order and with open-time params, the camera stays on YUY2. YUY2 at 1280x720 tops out at **10 fps**; 640x480 tops out at 30 fps. Setting `FOURCC`/`WIDTH`/`HEIGHT`/`FPS` returns without error but does not change the real mode.
2. **ffmpeg can.** `-f dshow -vcodec mjpeg -video_size 1280x720 -framerate 60` delivers about 51-60 frames/s on the same camera.
3. **The resolution you request must be a mode the camera really offers.** The bug that cost the most time at the end: `config.ini` had `width = 980` with `height = 720`. 980x720 is not a Brio mode, so ffmpeg failed with `Could not set video options` / `I/O error` and the app silently fell back to OpenCV at about 30 fps. Changing the width to 1280 gave 60 fps immediately. Always validate the requested size against the camera's mode list.
4. **Do the per-frame work on small frames.** At 1280x720 the app spent 23-30 ms per frame in Python/OpenCV (the 60 fps budget is 16.7 ms), so the loop ran 33-43 fps even though the camera delivered 60. Letting ffmpeg scale to 640x360 before Python sees the frame cut the work to about 8 ms per frame.

## 2. Architecture that worked

```
startup:
  OpenCV opens camera -> apply camera controls (exposure, gain, brightness, ...) -> read controls back/cache them -> release camera
  (controls persist in the camera hardware after OpenCV releases it - tested)
run:
  ffmpeg subprocess (dshow, MJPEG, 1280x720 @ 60, scale to 640x360, raw bgr24 on stdout)
    -> reader thread -> small queue (deque maxlen=2, drop oldest)
    -> app loop calls read() and gets the newest 640x360 frame
fallback:
  if ffmpeg yields no frames: print a WARNING and drop back to OpenCV (about 30 fps)
```

### ffmpeg command

```
ffmpeg -hide_banner -loglevel error -fflags nobuffer -flags low_delay ^
  -f dshow -vcodec mjpeg -video_size 1280x720 -framerate 60 -i video="Logitech BRIO" ^
  -vf scale=640:360 -f rawvideo -pix_fmt bgr24 -
```

- Use `creationflags=CREATE_NO_WINDOW` on Windows so no console window pops up.
- Frame size in bytes is `out_w * out_h * 3`. Read exactly that many bytes per frame from `stdout`.
- Keep only the newest frames (`deque(maxlen=2)`), count produced vs dropped, and drain `stderr` in its own thread so the pipe never blocks.
- First frame can take a moment: wait up to about 10 s at startup and about 3 s per `read()`.
- Terminate the subprocess on exit (`release()` plus `atexit`).

### Wrapper class shape (drop-in for `cv2.VideoCapture`)

`FfmpegCapture(device_name, width, height, fps, ffmpeg_path=None, cached_props=None, out_width=None, out_height=None)` exposes `read()`, `get(prop)`, `set(prop, val)` (returns False), `isOpened()` and `release()`. `get()` answers `FRAME_WIDTH`/`FRAME_HEIGHT` with the **output** size, `FPS` with the requested fps, `FOURCC` with MJPG, and the camera-control properties from the cached values taken before OpenCV released the camera. That keeps the rest of the app code unchanged.

## 3. Config options added

| key | default | meaning |
|---|---|---|
| `camera_name` | `Logitech BRIO` | DirectShow device name passed to ffmpeg |
| `ffmpeg_path` | empty | full path to ffmpeg.exe; empty means use PATH |
| `use_ffmpeg` | `1` | `0` forces the old OpenCV capture |
| `width` / `height` | `1280` / `720` | **camera** mode requested from ffmpeg (must be a real mode) |
| `fps` | `60` | requested camera fps |

ffmpeg must be on PATH (or `ffmpeg_path` set), also for the packaged exe.

## 4. The Brio's mode list is flaky

- The Brio sometimes exposes only 1920x1080 @ 30 (yuyv422/nv12). Then a direct MJPEG request fails with `Could not set video options`.
- The full list (MJPEG 1280x720 up to about 60 fps) comes back after setting 1280x720 / 60 in Logi Options+, closing the Logi+ preview, and (if needed) replugging the camera.
- The root cause of the revert was never proven. Logi+ (`logioptionsplus_agent`) uses the camera, and the camera is sometimes briefly busy (OpenCV reads zeros, ffmpeg returns an I/O error).
- Check the current list with:

```
ffmpeg -hide_banner -list_options true -f dshow -i video="Logitech BRIO" 2>&1 | Select-String "vcodec|pixel_format"
```

  Look for `vcodec=mjpeg ... 1280x720` with fps up to 60.
- Recommended for GolfSimVision: run this check at startup, and show a clear on-screen warning (not just a console line) when the app falls back to slow capture.
- The camera was used on an extension USB-A cable; a direct USB 3 port is worth trying if modes keep disappearing.

## 5. How to measure honestly

The on-screen "FPS" in the old app was misleading: "Actual FPS" is the main-loop iteration rate, "Fixed FPS" echoes the config, and "Detected FPS" echoes what the driver claims. Measure three things separately:

1. **Camera delivery rate** (frames produced per second by the ffmpeg reader) and dropped frames.
2. **Time waiting for a frame** in the loop (about 25 ms means a 30 fps source; about 0 means the loop is the bottleneck).
3. **Per-frame work** (copy/resize/color/track/imshow/waitKey, in ms).

Logging these every 5 seconds made the problem obvious: before, camera 60/s but loop 33-43/s with 23-30 ms of work; with the fix, 60 fps with about 8 ms of work. The run that exposed the width bug showed `wait for frame` about 25 ms and no "camera delivered" figure, meaning ffmpeg never started.

## 6. Other findings that matter

- **Camera controls persist**: set exposure/gain/brightness/etc. through OpenCV, release, then capture with ffmpeg; the settings stick (verified with a brightness test).
- **Auto exposure**: `CAP_PROP_AUTO_EXPOSURE` is unsupported on this camera (reads -1.0). Manual exposure of -8 delivered a steady 60/s. Frame-rate drops from 40 to 30 are consistent with the camera's own auto/low-light exposure lengthening frames, so fix exposure manually.
- **Duplicate-frame check**: the old app compared each frame with the previous one at full resolution. With ffmpeg delivering distinct frames this check is unnecessary and was skipped.
- **GPU**: ffmpeg here has `mjpeg_cuvid` and `scale_cuda` (RTX 4060 Laptop, driver 617.42). It was not adopted: Python received frames using only 3-4 % of one core, so GPU decode would not address the Python-side cost. Revisit only if the CPU path is not enough.
- **Keys**: the legacy app's keys are lowercase only (`a` Advanced Settings with the X/Y gate trackbars, `d` HSV debug, `q` quit). With Caps Lock on, `a` silently does nothing.
- **Laptop**: possible thermal/power throttling was suspected but the CPU model was never checked.

## 7. Checklist for GolfSimVision

1. Request only camera modes the device lists; validate `width x height @ fps` against `ffmpeg -list_options` before starting.
2. Capture MJPEG through ffmpeg (dshow), not through OpenCV, when you need 1280x720 @ 60 on this Brio.
3. Apply camera controls once through OpenCV at startup, release, then start ffmpeg.
4. Scale in ffmpeg to the size you actually track at.
5. Keep a drop-oldest queue so the app always gets the newest frame.
6. Fall back to OpenCV with a visible warning if ffmpeg yields no frames.
7. Log camera delivery rate, frame wait and per-frame work every few seconds.
8. Fix exposure manually; do not rely on auto exposure at 60 fps.
9. Document that Logi Options+ must be set to 1280x720 / 60 (preview closed) if the mode list disappears.

## 8. Reference: working values (legacy app)

`config.ini`: `width = 1280`, `height = 720`, `fps = 60`, `mjpeg = 1`, `exposure = -8.0`, `gain = 128`, `brightness = 147`, `contrast = 99`, `saturation = 128`, `whitebalanceblue = 3980`, `zoom = 100`, `focus = 10`, `autofocus = 1`.

Tracking coordinates (`startx1`, `startx2`, `y1`, `y2`, `radius`) are in the **640x360** tracking frame, not in camera pixels.

Reference implementation: `ball_tracking.py` in `D:\GSPro Putting\cam-putting-py-main` (see the `FfmpegCapture` class and the capture block that follows the camera-control section). A copy of the working state is in `working-backup-2026-10-14`.
