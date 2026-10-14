using System;

namespace GolfSimVision.Capture;

/// <summary>Camera mode requested from the device -- must be a real mode the camera
/// offers (CAMERA_CAPABILITIES.md: a requested-but-unsupported size/fps fails or
/// silently falls back, never raises on its own).</summary>
public sealed record CameraOpenRequest(int Index, int Width, int Height, int Fps, double? Exposure, double? Gain);

/// <summary>What the camera actually granted, read back after every property set --
/// never trust what was requested (CAMERA_CAPABILITIES.md, capture.py's open_camera()).
/// </summary>
public sealed record CameraOpenResult(
    bool Success,
    string? FailureReason,
    int ActualWidth,
    int ActualHeight,
    double ActualFps,
    bool ExposureLocked,
    string ExposureNote);

/// <summary>
/// A video source behind one interface (ADR-0004). UsbWebcam is the only
/// implementation this ticket builds; IPhoneCamera is reserved for v2.
/// </summary>
public interface IFrameSource : IDisposable
{
    bool IsOpen { get; }

    CameraOpenResult Open(CameraOpenRequest request);
    void Close();

    /// Fires for every captured frame -- the full-rate measurement stream.
    Action<CapturedFrame>? OnFrame { get; set; }

    /// Fires at a throttled, best-effort rate -- JPEG-encoded, for the UI's live preview.
    Action<byte[]>? OnPreviewFrame { get; set; }

    /// Fires on every real measured-fps reading (not the driver's own readback), and
    /// when delivery looks degraded relative to what was requested.
    Action<CaptureHealth>? OnHealth { get; set; }

    /// Timestamped diagnostic lines -- camera open/property-set calls can each take up
    /// to ~15s on known hardware and look identical to a hang without this.
    Action<string>? OnLog { get; set; }
}

/// <summary>Real, measured delivery health -- never the driver's self-reported FPS.</summary>
public sealed record CaptureHealth(double MeasuredFps, bool IsDegraded, string? DegradedReason);
