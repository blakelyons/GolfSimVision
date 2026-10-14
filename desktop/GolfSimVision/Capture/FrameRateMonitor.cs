using System;
using System.Collections.Generic;
using System.Linq;

namespace GolfSimVision.Capture;

/// <summary>
/// Measures real delivery rate from actual frame timestamps -- never from a driver's
/// CAP_PROP_FPS readback, which just echoes back whatever was last requested
/// (CAMERA_CAPABILITIES.md). Pure logic, no camera involved -- fully unit-testable,
/// mirroring capture.py's jitter_report but simplified to what this ticket's acceptance
/// criterion needs: a real measured fps, logged periodically.
/// </summary>
public sealed class FrameRateMonitor
{
    private readonly int _windowSize;
    private readonly double _degradedFraction;
    private readonly Queue<double> _timestamps = new();

    /// <param name="targetFps">What was requested -- degradation is judged relative to this.</param>
    /// <param name="windowSize">How many recent inter-frame intervals to measure over.</param>
    /// <param name="degradedFraction">Measured fps below targetFps * this fraction counts as degraded.</param>
    public FrameRateMonitor(double targetFps, int windowSize = 30, double degradedFraction = 0.8)
    {
        TargetFps = targetFps;
        _windowSize = windowSize;
        _degradedFraction = degradedFraction;
    }

    public double TargetFps { get; }

    /// <summary>Records a frame's timestamp. Returns a health reading once enough
    /// samples exist to measure from, or null if still warming up.</summary>
    public CaptureHealth? RecordFrame(double timestampSeconds)
    {
        _timestamps.Enqueue(timestampSeconds);
        while (_timestamps.Count > _windowSize + 1) _timestamps.Dequeue();

        if (_timestamps.Count < 2) return null;

        var ordered = _timestamps.ToArray();
        var elapsed = ordered[^1] - ordered[0];
        if (elapsed <= 0) return null;

        var measuredFps = (ordered.Length - 1) / elapsed;
        var isDegraded = measuredFps < TargetFps * _degradedFraction;
        var reason = isDegraded
            ? $"measured {measuredFps:F1}fps, requested {TargetFps:F0}fps"
            : null;

        return new CaptureHealth(measuredFps, isDegraded, reason);
    }
}
