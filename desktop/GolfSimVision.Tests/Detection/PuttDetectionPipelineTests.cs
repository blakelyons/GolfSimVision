using System;
using System.Collections.Generic;
using GolfSimVision.Calibration;
using GolfSimVision.Detection;
using GolfSimVision.Zones;
using Xunit;

namespace GolfSimVision.Tests.Detection;

public class PuttDetectionPipelineTests
{
    /// Scripted detections, one per ProcessFrame call, by frame index.
    private sealed class FakeBallDetector : IBallDetector
    {
        private readonly Queue<BallDetection?> _script;

        public FakeBallDetector(IEnumerable<BallDetection?> script) => _script = new Queue<BallDetection?>(script);

        public BallDetection? Detect(byte[] bgrBytes, int width, int height) => _script.Dequeue();
    }

    private sealed class FakeCalibrationProvider : ICalibrationProvider
    {
        public CalibrationData? Current { get; set; }
    }

    private sealed class TestClock
    {
        public double Seconds { get; set; }
        public double Now() => Seconds;
        public void Advance(double dt) => Seconds += dt;
    }

    private const double Ppm = 2.0;

    private static BallDetection At(double x, double ppm = Ppm) => new(x, 0.5, 0.02, 0.02, 0.9, ppm);

    /// Runs a scripted sequence through the pipeline, advancing the fake clock between
    /// frames so the zone state machine's own duration-based quality gate doesn't
    /// spuriously reject (every real frame has nonzero time between it and the last).
    private static PuttDetectionOutcome? RunScript(PuttDetectionPipeline pipeline, TestClock clock, IReadOnlyList<BallDetection?> script)
    {
        PuttDetectionOutcome? outcome = null;
        foreach (var _ in script)
        {
            clock.Advance(0.05);
            outcome = pipeline.ProcessFrame(Array.Empty<byte>(), 1280, 720);
            if (outcome is not null) break;
        }
        return outcome;
    }

    private static BallDetection?[] FullPuttScript(double ppm = Ppm) => new BallDetection?[]
    {
        At(0.15, ppm), // Idle -> Waiting
        At(0.35, ppm), // Waiting -> Tracking (exits zone right)
        At(0.42, ppm),
        At(0.50, ppm),
        At(0.58, ppm),
        At(0.70, ppm), // crosses gateway -> finalize
    };

    [Fact]
    public void ReturnsNullWhileNoPuttHasFinalized()
    {
        var detector = new FakeBallDetector(new BallDetection?[] { At(0.15) });
        var pipeline = new PuttDetectionPipeline(
            detector,
            new ZoneStateMachine(ZoneConfig.Default),
            new FakeCalibrationProvider());

        var result = pipeline.ProcessFrame(Array.Empty<byte>(), 1280, 720);

        Assert.Null(result);
    }

    [Fact]
    public void RejectsWhenNoCalibrationHasBeenPerformed()
    {
        var clock = new TestClock();
        var detector = new FakeBallDetector(FullPuttScript());
        var pipeline = new PuttDetectionPipeline(
            detector,
            new ZoneStateMachine(ZoneConfig.Default, clock: clock.Now),
            new FakeCalibrationProvider { Current = null });

        var outcome = RunScript(pipeline, clock, FullPuttScript());

        var rejected = Assert.IsType<PuttDetectionOutcome.Rejected>(outcome);
        Assert.Contains("no calibration", rejected.Reason);
    }

    private static PuttMeasurement MeasureWithCalibration(double millimetersPerPixel)
    {
        var clock = new TestClock();
        // A reasonable per-detection ppm (2.0, same as ZoneStateMachineTests' own safe
        // fixture) so the zone state machine's own displacement/heading gates pass
        // regardless -- isolating this test to the one thing it's checking: whether the
        // pipeline uses calibration's scale for the final measurement.
        var detector = new FakeBallDetector(FullPuttScript());
        var calibration = new FakeCalibrationProvider
        {
            Current = new CalibrationData(
                new HomographyMatrix(new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }),
                millimetersPerPixel,
                Version: 1,
                CalibratedAtUtc: DateTimeOffset.UtcNow),
        };
        var pipeline = new PuttDetectionPipeline(detector, new ZoneStateMachine(ZoneConfig.Default, clock: clock.Now), calibration);

        var outcome = RunScript(pipeline, clock, FullPuttScript());
        return Assert.IsType<PuttDetectionOutcome.Measured>(outcome).Measurement;
    }

    [Fact]
    public void MeasuredSpeedScalesWithCalibrationsMillimetersPerPixelNotTheTracksOwnPpm()
    {
        // Regression test for a real bug caught while writing this pipeline:
        // CalibrationData stores mm-PER-pixel, but PuttCalculator expects pixels-PER-mm
        // -- the reciprocal. PuttCalculator's xs_mm = pixels / pixelsPerMm, so speed is
        // directly proportional to millimetersPerPixel: doubling it means each pixel of
        // on-screen movement represents twice the real-world distance, so the same
        // pixel-space track must come out at twice the speed. The per-detection ppm the
        // fake detector supplies never changes between the two runs below, so this ratio
        // can only come from the pipeline actually consuming calibration's value.
        var atHalfMmPerPixel = MeasureWithCalibration(0.5);
        var atDoubleMmPerPixel = MeasureWithCalibration(1.0);

        Assert.Equal(atHalfMmPerPixel.SpeedMph * 2, atDoubleMmPerPixel.SpeedMph, precision: 1);
    }
}
