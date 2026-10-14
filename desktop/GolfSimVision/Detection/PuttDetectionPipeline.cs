using System;
using System.Collections.Generic;
using GolfSimVision.Calibration;

namespace GolfSimVision.Detection;

/// <summary>The ticket's named outcome type -- a real putt produces either this or a
/// rejection, never silence.</summary>
public sealed record PuttMeasurement(double SpeedMph, double HlaDegrees, double DistanceFt, double SpeedFps, int TrackPointCount);

public abstract record PuttDetectionOutcome
{
    public sealed record Measured(PuttMeasurement Measurement) : PuttDetectionOutcome;

    public sealed record Rejected(string Reason) : PuttDetectionOutcome;
}

/// <summary>
/// Wires BallDetector -> ZoneStateMachine -> PuttCalculator into one per-frame call,
/// consuming the real ZoneConfig (ticket #4) and the real calibration (ticket #5)
/// rather than a per-putt ball-radius guess -- ADR-0005's explicit requirement.
/// </summary>
public sealed class PuttDetectionPipeline
{
    private readonly IBallDetector _detector;
    private readonly ZoneStateMachine _zoneStateMachine;
    private readonly ICalibrationProvider _calibration;

    public PuttDetectionPipeline(IBallDetector detector, ZoneStateMachine zoneStateMachine, ICalibrationProvider calibration)
    {
        _detector = detector;
        _zoneStateMachine = zoneStateMachine;
        _calibration = calibration;
    }

    /// <summary>Feed one frame. Returns an outcome only when a putt finalizes this
    /// frame (measured or rejected); null on every other frame -- most of them.</summary>
    public PuttDetectionOutcome? ProcessFrame(byte[] bgrBytes, int width, int height)
    {
        var detection = _detector.Detect(bgrBytes, width, height);
        var finalize = _zoneStateMachine.Update(detection);

        return finalize switch
        {
            null => null,
            PuttFinalizeResult.Rejected r => new PuttDetectionOutcome.Rejected(r.Reason),
            PuttFinalizeResult.TrackReady t => BuildMeasurement(t.Track),
            _ => new PuttDetectionOutcome.Rejected("unrecognized finalize result"),
        };
    }

    private PuttDetectionOutcome BuildMeasurement(IReadOnlyList<TrackPoint> track)
    {
        if (_calibration.Current is not { MillimetersPerPixel: > 0 } calibration)
        {
            return new PuttDetectionOutcome.Rejected("no calibration performed yet -- cannot compute a real-world scale");
        }

        // Units differ at this one seam and nowhere else: CalibrationData stores
        // mm-per-pixel (ADR-0005's own unit for a homography-derived scale), while
        // PuttCalculator (an unmodified port of putt_calculator.py) takes pixels-per-mm,
        // matching detector.py's BallDetection.pixels_per_mm convention throughout the rest
        // of this module. Inverted once, here, deliberately -- not left implicit.
        var pixelsPerMm = 1.0 / calibration.MillimetersPerPixel;

        try
        {
            var result = PuttCalculator.Calculate(
                track, _zoneStateMachine.FrameWidth, _zoneStateMachine.FrameHeight, pixelsPerMm);
            return new PuttDetectionOutcome.Measured(new PuttMeasurement(
                result.SpeedMph, result.HlaDegrees, result.DistanceFt, result.SpeedFps, track.Count));
        }
        catch (PuttCalculationException ex)
        {
            return new PuttDetectionOutcome.Rejected(ex.Message);
        }
    }
}
