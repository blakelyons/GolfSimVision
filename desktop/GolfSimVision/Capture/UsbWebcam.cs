using System;
using System.Runtime.InteropServices;
using System.Threading;
using OpenCvSharp;

namespace GolfSimVision.Capture;

/// <summary>
/// ADR-0004's reference FrameSource implementation. Captures via Media Foundation
/// (CAP_MSMF), never DirectShow (CAP_DSHOW) -- CAMERA_CAPABILITIES.md confirmed
/// CAP_DSHOW silently caps this hardware at ~30fps regardless of what's requested, real
/// measured frame intervals never lie, CAP_MSMF gets a genuine ~60fps on the same
/// camera. Never trusts a requested setting -- reads back and reports what the camera
/// actually granted (capture.py's open_camera(), same principle).
///
/// The capture loop itself isn't unit tested here, the same as capture.py's own
/// interactive loop isn't -- it needs a physical camera. FrameRateMonitor and RingBuffer
/// are the pure, testable pieces this class delegates to.
/// </summary>
public sealed class UsbWebcam : IFrameSource
{
    private const int PreviewIntervalMs = 66; // ~15fps best-effort preview (ADR-0003)
    private const double HealthIntervalSeconds = 1.0; // don't flood the UI at frame rate

    private VideoCapture? _capture;
    private Thread? _captureThread;
    private volatile bool _stopRequested;
    private FrameRateMonitor? _rateMonitor;
    private double _lastPreviewAt;
    private double _lastHealthEmitAt;
    private bool? _lastReportedDegraded;

    public bool IsOpen => _capture?.IsOpened() ?? false;

    public Action<CapturedFrame>? OnFrame { get; set; }
    public Action<byte[]>? OnPreviewFrame { get; set; }
    public Action<CaptureHealth>? OnHealth { get; set; }
    public Action<string>? OnLog { get; set; }

    public CameraOpenResult Open(CameraOpenRequest request)
    {
        var t0 = DateTime.UtcNow;
        void Log(string msg) => OnLog?.Invoke($"[{(DateTime.UtcNow - t0).TotalSeconds,5:F1}s] {msg}");

        Log("opening camera (CAP_MSMF) -- this can take up to ~15s on this hardware");
        var capture = new VideoCapture(request.Index, VideoCaptureAPIs.MSMF);
        Log($"opened, isOpened={capture.IsOpened()}");

        if (!capture.IsOpened())
        {
            capture.Dispose();
            return new CameraOpenResult(false, "camera failed to open", 0, 0, 0, false, "");
        }

        // No FOURCC request: CAP_PROP_FOURCC always reports rejection under CAP_MSMF on
        // the reference hardware, and the readback is unusable -- see
        // docs/CAMERA_CAPABILITIES.md. Real frames still decode fine regardless.
        Log("requesting width (can take up to ~15s)");
        capture.Set(VideoCaptureProperties.FrameWidth, request.Width);
        Log("requesting height (can take up to ~15s)");
        capture.Set(VideoCaptureProperties.FrameHeight, request.Height);
        Log("requesting fps (can take up to ~15s)");
        capture.Set(VideoCaptureProperties.Fps, request.Fps);

        var exposureLocked = false;
        var exposureNote = "";
        if (request.Exposure is { } exposure)
        {
            // The correct manual/auto sentinel for CAP_PROP_AUTO_EXPOSURE is backend- and
            // camera-specific and was never empirically re-confirmed for CAP_MSMF on this
            // hardware (CAMERA_CAPABILITIES.md only verified it for the Phase 0 corpus
            // tool's own backend choice) -- 0.25 is OpenCV's documented "manual" sentinel
            // on most Windows backends, but this must be confirmed against real hardware,
            // not assumed correct from this note alone.
            capture.Set(VideoCaptureProperties.AutoExposure, 0.25);
            capture.Set(VideoCaptureProperties.Exposure, exposure);
            if (request.Gain is { } gain) capture.Set(VideoCaptureProperties.Gain, gain);
            exposureLocked = true;
            exposureNote = $"exposure={exposure}, gain={request.Gain?.ToString() ?? "(unset)"} -- auto-exposure sentinel unverified on this hardware";
        }

        Log("done setting properties, reading back actual values");
        var actualWidth = (int)capture.Get(VideoCaptureProperties.FrameWidth);
        var actualHeight = (int)capture.Get(VideoCaptureProperties.FrameHeight);
        var actualFps = capture.Get(VideoCaptureProperties.Fps);

        _capture = capture;
        _rateMonitor = new FrameRateMonitor(request.Fps > 0 ? request.Fps : actualFps);
        _stopRequested = false;
        _captureThread = new Thread(CaptureLoop) { IsBackground = true, Name = "usb-webcam-capture" };
        _captureThread.Start();

        return new CameraOpenResult(true, null, actualWidth, actualHeight, actualFps, exposureLocked, exposureNote);
    }

    public void Close()
    {
        // Known limitation, same as any blocking-read capture loop: if capture.Read()
        // is already blocked waiting on a frame when _stopRequested flips, the thread
        // only notices on its next loop iteration. On this hardware frames arrive every
        // ~16ms at 60fps, so a 3s join is generous; a genuinely hung camera could still
        // race Dispose() below against an in-flight Read() on the capture thread.
        _stopRequested = true;
        _captureThread?.Join(TimeSpan.FromSeconds(3));
        _captureThread = null;
        _capture?.Release();
        _capture?.Dispose();
        _capture = null;
    }

    private void CaptureLoop()
    {
        var capture = _capture;
        var rateMonitor = _rateMonitor;
        if (capture is null || rateMonitor is null) return;

        using var mat = new Mat();
        while (!_stopRequested)
        {
            bool ok;
            try
            {
                ok = capture.Read(mat);
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"capture read failed: {ex.Message}, stopping");
                return;
            }
            var timestamp = DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond;
            if (!ok || mat.Empty()) continue;

            // OnFrame (ring buffer + detection + ONNX inference + a possible GSPro send)
            // is isolated in its own try/catch, separate from health reporting and the
            // preview send below -- a repeated failure in OnFrame (e.g. every real frame
            // hitting the same detection bug) must never be able to starve the live
            // preview just because it happens to run afterward in the same loop. Each of
            // these three is an independent concern; one failing silently must not take
            // the others down with it. All of it still runs on this background thread
            // with no other safety net -- an unhandled exception on any thread, not just
            // the UI thread, terminates the whole .NET process immediately.
            byte[] bytes;
            try
            {
                bytes = MatToBgrBytes(mat);
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"frame conversion failed, skipping this frame: {ex}");
                continue;
            }

            try
            {
                OnFrame?.Invoke(new CapturedFrame(timestamp, mat.Width, mat.Height, bytes));
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"OnFrame handler failed (ring buffer/detection), continuing: {ex}");
            }

            try
            {
                var health = rateMonitor.RecordFrame(timestamp);
                if (health is not null) MaybeReportHealth(health, timestamp);
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"health reporting failed, continuing: {ex}");
            }

            try
            {
                if (OnPreviewFrame is not null && timestamp - _lastPreviewAt >= PreviewIntervalMs / 1000.0)
                {
                    _lastPreviewAt = timestamp;
                    Cv2.ImEncode(".jpg", mat, out var jpegBytes, new ImageEncodingParam(ImwriteFlags.JpegQuality, 70));
                    OnPreviewFrame(jpegBytes);
                }
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"preview encode/send failed, continuing: {ex}");
            }
        }
    }

    /// Throttles health reporting to about once a second -- FrameRateMonitor returns a
    /// reading on nearly every frame once warmed up, and posting a UI message at full
    /// frame rate would flood the WebView2 bridge for no benefit. A degradation onset is
    /// still reported immediately rather than waiting out the interval.
    private void MaybeReportHealth(CaptureHealth health, double timestamp)
    {
        var degradedChanged = _lastReportedDegraded != health.IsDegraded;
        if (!degradedChanged && timestamp - _lastHealthEmitAt < HealthIntervalSeconds) return;

        _lastHealthEmitAt = timestamp;
        _lastReportedDegraded = health.IsDegraded;
        OnHealth?.Invoke(health);
    }

    private static byte[] MatToBgrBytes(Mat mat)
    {
        // mat is the capture loop's single reused buffer -- never dispose it here, only
        // the clone made when it isn't already memory-contiguous.
        Mat? clone = null;
        var source = mat;
        if (!mat.IsContinuous())
        {
            clone = mat.Clone();
            source = clone;
        }
        try
        {
            var length = (int)(source.Total() * source.ElemSize());
            var buffer = new byte[length];
            Marshal.Copy(source.Data, buffer, 0, length);
            return buffer;
        }
        finally
        {
            clone?.Dispose();
        }
    }

    public void Dispose() => Close();
}
