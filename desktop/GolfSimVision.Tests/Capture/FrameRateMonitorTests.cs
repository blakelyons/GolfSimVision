using GolfSimVision.Capture;
using Xunit;

namespace GolfSimVision.Tests.Capture;

public class FrameRateMonitorTests
{
    [Fact]
    public void ReturnsNullUntilAtLeastTwoSamples()
    {
        var monitor = new FrameRateMonitor(targetFps: 60);
        Assert.Null(monitor.RecordFrame(0.0));
    }

    [Fact]
    public void MeasuresRealFpsFromTimestampsNotADriverReadback()
    {
        var monitor = new FrameRateMonitor(targetFps: 60);
        // 60 evenly-spaced frames at exactly 1/60s apart -> measured fps should read ~60,
        // regardless of what any "requested" value claims.
        CaptureHealth? last = null;
        for (var i = 0; i < 60; i++)
        {
            last = monitor.RecordFrame(i / 60.0);
        }

        Assert.NotNull(last);
        Assert.InRange(last!.MeasuredFps, 59.0, 61.0);
        Assert.False(last.IsDegraded);
    }

    [Fact]
    public void FlagsDegradedWhenMeasuredFpsFallsWellBelowTarget()
    {
        var monitor = new FrameRateMonitor(targetFps: 60, degradedFraction: 0.8);
        // ~30fps delivery against a 60fps target/request -- mirrors the real CAP_DSHOW
        // ceiling CAMERA_CAPABILITIES.md found on this hardware.
        CaptureHealth? last = null;
        for (var i = 0; i < 30; i++)
        {
            last = monitor.RecordFrame(i / 30.0);
        }

        Assert.NotNull(last);
        Assert.True(last!.IsDegraded);
        Assert.Contains("30", last.DegradedReason);
    }

    [Fact]
    public void OnlyMeasuresOverTheConfiguredWindow()
    {
        // A monitor with a small window should "forget" an old slow stretch once enough
        // fast frames have pushed it out of the window.
        var monitor = new FrameRateMonitor(targetFps: 60, windowSize: 5);
        for (var i = 0; i < 5; i++) monitor.RecordFrame(i * 1.0); // ~1fps, degraded

        CaptureHealth? last = null;
        for (var i = 0; i < 10; i++) last = monitor.RecordFrame(5.0 + i / 60.0); // back to 60fps

        Assert.NotNull(last);
        Assert.InRange(last!.MeasuredFps, 55.0, 65.0);
        Assert.False(last.IsDegraded);
    }
}
