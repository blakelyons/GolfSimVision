using System.Collections.Generic;
using GolfSimVision.Detection;
using Xunit;

namespace GolfSimVision.Tests.Detection;

/// <summary>
/// Mirrors putt_calculator.py's behavior -- no existing Python test suite to compare
/// against (ported "unmodified" from V1, none written there either). ppm=2.0 px/mm
/// throughout for round mm-per-normalized-unit math: a frame_width of 1280 makes a
/// normalized-x delta d equal to d * 1280 / 2.0 = d * 640 mm.
/// </summary>
public class PuttCalculatorTests
{
    private const double Ppm = 2.0;
    private const int FrameWidth = 1280;
    private const int FrameHeight = 720;

    private static TrackPoint P(double x, double y, double t, double ppm = Ppm) => new(x, y, ppm, t);

    [Fact]
    public void ThrowsWithFewerThanTwoPoints()
    {
        var track = new List<TrackPoint> { P(0.1, 0.5, 0.0) };

        Assert.Throws<PuttCalculationException>(() => PuttCalculator.Calculate(track, FrameWidth, FrameHeight));
    }

    [Fact]
    public void ThrowsWhenElapsedTimeIsNotPositive()
    {
        var track = new List<TrackPoint> { P(0.1, 0.5, 1.0), P(0.3, 0.5, 1.0) };

        Assert.Throws<PuttCalculationException>(() => PuttCalculator.Calculate(track, FrameWidth, FrameHeight));
    }

    [Fact]
    public void TwoPointTrackComputesStraightLineSpeedAndHla()
    {
        // 0.2 normalized-x over 0.1s = 128mm over 0.1s = 1.28 m/s = ~2.86mph, HLA=0 (no y change).
        var track = new List<TrackPoint> { P(0.10, 0.5, 0.0), P(0.30, 0.5, 0.1) };

        var result = PuttCalculator.Calculate(track, FrameWidth, FrameHeight);

        Assert.Equal(0.0, result.HlaDegrees, precision: 1);
        Assert.True(result.SpeedMph > 0);
        Assert.Equal(0.0, result.SpinTotal);
        Assert.Equal(0.0, result.SpinAxis);
    }

    [Fact]
    public void UsesManualPpmInsteadOfTrackAverageWhenProvided()
    {
        // Two tracks with wildly different per-point ppm, but the same manualPpm
        // override, should produce identical results -- the per-point ppm is ignored.
        var trackA = new List<TrackPoint> { P(0.10, 0.5, 0.0, ppm: 0.5), P(0.30, 0.5, 0.1, ppm: 0.5) };
        var trackB = new List<TrackPoint> { P(0.10, 0.5, 0.0, ppm: 50.0), P(0.30, 0.5, 0.1, ppm: 50.0) };

        var resultA = PuttCalculator.Calculate(trackA, FrameWidth, FrameHeight, manualPpm: 2.0);
        var resultB = PuttCalculator.Calculate(trackB, FrameWidth, FrameHeight, manualPpm: 2.0);

        Assert.Equal(resultA.SpeedMph, resultB.SpeedMph);
        Assert.Equal(2.0, resultA.PixelsPerMm);
    }

    [Fact]
    public void AppliesHlaCalibrationOffset()
    {
        // A pure-rightward track (HLA=0 uncalibrated) with a +5 degree offset should
        // read back as -5.
        var track = new List<TrackPoint> { P(0.10, 0.5, 0.0), P(0.30, 0.5, 0.1), P(0.50, 0.5, 0.2) };

        var result = PuttCalculator.Calculate(track, FrameWidth, FrameHeight, hlaCalibrationOffsetDegrees: 5.0);

        Assert.Equal(-5.0, result.HlaDegrees, precision: 1);
    }

    [Fact]
    public void RejectsImplausiblySlowSpeed()
    {
        // A tiny displacement over a long time -- far below MinPlausibleSpeedMph.
        var track = new List<TrackPoint> { P(0.300, 0.5, 0.0), P(0.301, 0.5, 5.0) };

        var ex = Assert.Throws<PuttCalculationException>(() => PuttCalculator.Calculate(track, FrameWidth, FrameHeight));
        Assert.Contains("Implausible speed", ex.Message);
    }

    [Fact]
    public void RejectsImplausiblyFastSpeed()
    {
        // A huge displacement in a tiny amount of time -- far above MaxPlausibleSpeedMph.
        var track = new List<TrackPoint> { P(0.05, 0.5, 0.0), P(0.95, 0.5, 0.01) };

        var ex = Assert.Throws<PuttCalculationException>(() => PuttCalculator.Calculate(track, FrameWidth, FrameHeight));
        Assert.Contains("Implausible speed", ex.Message);
    }

    [Fact]
    public void RejectsImplausibleHla()
    {
        // Mostly-sideways movement -- a steep angle, exceeding MaxPlausibleHlaDegrees.
        var track = new List<TrackPoint> { P(0.30, 0.10, 0.0), P(0.32, 0.70, 0.1), P(0.34, 1.20, 0.2) };

        var ex = Assert.Throws<PuttCalculationException>(() => PuttCalculator.Calculate(track, FrameWidth, FrameHeight));
        Assert.Contains("Implausible HLA", ex.Message);
    }

    [Fact]
    public void ThreeOrMorePointsUseALinearVelocityFitNotJustEndpoints()
    {
        // A perfectly linear, evenly-spaced track: the polyfit-based (3+ point) path and
        // the endpoint-only (2-point) path should agree closely on speed for data this
        // clean, since both are measuring the same constant velocity.
        var twoPoint = new List<TrackPoint> { P(0.10, 0.5, 0.0), P(0.50, 0.5, 0.2) };
        var threePoint = new List<TrackPoint> { P(0.10, 0.5, 0.0), P(0.30, 0.5, 0.1), P(0.50, 0.5, 0.2) };

        var twoPointResult = PuttCalculator.Calculate(twoPoint, FrameWidth, FrameHeight);
        var threePointResult = PuttCalculator.Calculate(threePoint, FrameWidth, FrameHeight);

        Assert.Equal(twoPointResult.SpeedMph, threePointResult.SpeedMph, precision: 1);
    }

    [Fact]
    public void DistanceFtUsesFullTrackDisplacementNotJustTheMotionWindow()
    {
        // Points after MOTION_WINDOW_S (0.75s) are excluded from the velocity fit but
        // must still count toward the reported total distance.
        var track = new List<TrackPoint>
        {
            P(0.10, 0.5, 0.0),
            P(0.20, 0.5, 0.1),
            P(0.30, 0.5, 0.2),
            P(0.60, 0.5, 2.0), // well past the 0.75s motion window
        };

        var result = PuttCalculator.Calculate(track, FrameWidth, FrameHeight);

        // Full-track displacement: (0.60-0.10)*640 = 320mm = 1.0498ft.
        Assert.Equal(1.05, result.DistanceFt, precision: 2);
    }
}
