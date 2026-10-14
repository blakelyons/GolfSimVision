using System;
using System.Collections.Generic;
using System.Linq;

namespace GolfSimVision.Detection;

/// <summary>Thrown when a track's timing/scale is invalid, or the resulting speed/HLA is
/// outside physically-plausible bounds for a putt -- callers should treat this as "no
/// shot," matching putt_calculator.py's single ValueError failure contract.</summary>
public sealed class PuttCalculationException : Exception
{
    public PuttCalculationException(string message) : base(message)
    {
    }
}

public sealed record PuttResult(
    double SpeedMph,
    double HlaDegrees,
    double PixelsPerMm,
    double DistanceFt,
    double SpeedFps,
    double SpinTotal = 0.0,
    double SpinAxis = 0.0);

/// <summary>
/// Ported from prototypes/webcam-putting/detection/putt_calculator.py, unmodified.
/// Pure function of the track's own (x, y, pixels_per_mm, timestamp) tuples -- no
/// wall-clock reads, no state, so it needs no adaptation for any calling context.
/// Spin is always fabricated as zero (the Python original's "zero" spin_mode) --
/// GolfSimVision never measures spin; ADR-0007 requires the UI be honest that this is
/// a fabricated placeholder, never implied to be measured.
/// </summary>
public static class PuttCalculator
{
    private const double MmPerFoot = 304.8;

    // Only the first MotionWindowSeconds of track data is used for velocity fitting --
    // after this window the ball may have stopped rolling, and including stopped-ball
    // points in the linear fit drags the velocity estimate toward zero.
    private const double MotionWindowSeconds = 0.75;

    // Physical-plausibility bounds for a real putt. Outside these bounds, a result is
    // not a putt -- it's noise from a thin/short track being read as a real roll.
    public const double MinPlausibleSpeedMph = 0.5;
    public const double MaxPlausibleSpeedMph = 25.0;
    public const double MaxPlausibleHlaDegrees = 40.0;

    public static PuttResult Calculate(
        IReadOnlyList<TrackPoint> track,
        int frameWidth,
        int frameHeight,
        double manualPpm = 0.0,
        double hlaCalibrationOffsetDegrees = 0.0)
    {
        if (track.Count < 2) throw new PuttCalculationException("Need at least 2 track points");

        var avgPpm = manualPpm == 0.0 ? track.Average(p => p.PixelsPerMm) : manualPpm;
        var dt = track[^1].TimestampSeconds - track[0].TimestampSeconds;
        if (dt <= 0 || avgPpm <= 0) throw new PuttCalculationException("Invalid track timing or scale");

        var xsMm = track.Select(p => p.X * frameWidth / avgPpm).ToList();
        var ysMm = track.Select(p => p.Y * frameHeight / avgPpm).ToList();
        var ts = track.Select(p => p.TimestampSeconds).ToList();

        var tCutoff = ts[0] + MotionWindowSeconds;
        var motionIdx = ts.FindIndex(t => t > tCutoff);
        if (motionIdx < 0) motionIdx = ts.Count;
        motionIdx = Math.Max(motionIdx, 2);

        var xsFit = xsMm.Take(motionIdx).ToList();
        var ysFit = ysMm.Take(motionIdx).ToList();
        var tsFit = ts.Take(motionIdx).ToList();

        double speedMs;
        double hla;

        if (xsFit.Count >= 3)
        {
            var t0 = tsFit[0];
            var tsRel = tsFit.Select(t => t - t0).ToList();
            var vx = LinearFit.Slope(tsRel, xsFit);
            var vy = LinearFit.Slope(tsRel, ysFit);
            speedMs = Math.Sqrt((vx * vx) + (vy * vy)) / 1000.0;

            var xRange = xsFit[^1] - xsFit[0];
            if (Math.Abs(xRange) >= 10.0)
            {
                var slope = LinearFit.Slope(xsFit, ysFit);
                hla = Math.Atan2(slope, 1.0) * 180.0 / Math.PI;
            }
            else
            {
                hla = Math.Atan2(ysFit[^1] - ysFit[0], xsFit[^1] - xsFit[0]) * 180.0 / Math.PI;
            }
        }
        else
        {
            var dxMm = xsFit[^1] - xsFit[0];
            var dyMm = ysFit[^1] - ysFit[0];
            var distMm = Math.Sqrt((dxMm * dxMm) + (dyMm * dyMm));
            var dtFit = Math.Max(tsFit[^1] - tsFit[0], 1e-6);
            speedMs = distMm / 1000.0 / dtFit;
            hla = Math.Atan2(dyMm, dxMm) * 180.0 / Math.PI;
        }

        var speedMph = speedMs * 2.23694;
        hla -= hlaCalibrationOffsetDegrees;

        // Net displacement over the WHOLE track (not just the fit window) -- distinct
        // from the velocity/HLA fit above, exactly as in the Python original.
        var fullDxMm = xsMm[^1] - xsMm[0];
        var fullDyMm = ysMm[^1] - ysMm[0];
        var fullDistMm = Math.Sqrt((fullDxMm * fullDxMm) + (fullDyMm * fullDyMm));

        var distanceFt = Math.Round(fullDistMm / MmPerFoot, 2);
        var speedFps = Math.Round(speedMs * 3.28084, 2);
        speedMph = Math.Round(speedMph, 2);
        hla = Math.Round(hla, 2);

        if (speedMph is < MinPlausibleSpeedMph or > MaxPlausibleSpeedMph)
        {
            throw new PuttCalculationException(
                $"Implausible speed {speedMph} mph -- outside [{MinPlausibleSpeedMph}, {MaxPlausibleSpeedMph}] mph");
        }
        if (Math.Abs(hla) > MaxPlausibleHlaDegrees)
        {
            throw new PuttCalculationException($"Implausible HLA {hla} deg -- outside +/-{MaxPlausibleHlaDegrees} deg");
        }

        return new PuttResult(
            SpeedMph: speedMph,
            HlaDegrees: hla,
            PixelsPerMm: Math.Round(avgPpm, 4),
            DistanceFt: distanceFt,
            SpeedFps: speedFps);
    }
}
