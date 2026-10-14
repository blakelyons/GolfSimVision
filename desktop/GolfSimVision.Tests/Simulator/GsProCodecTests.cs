using GolfSimVision.Simulator;
using Xunit;

namespace GolfSimVision.Tests.Simulator;

/// Mirrors prototypes/gspro-harness/test_protocol.py's TestGSProCodec.
public class GsProCodecTests
{
    private int _counter = 100;

    private GsProCodec CreateCodec() => new("GolfSimVision", () => _counter++);

    [Fact]
    public void BuildPuttDefaultsSpinToRollingEstimate()
    {
        var payload = CreateCodec().BuildPutt(speed: 5.0, hla: -1.5, vla: 0.0);

        Assert.Equal(PuttSpin.RollingSpinRpm(5.0), payload.BallData.BackSpin);
        Assert.Equal(PuttSpin.RollingSpinRpm(5.0), payload.BallData.TotalSpin);
        Assert.Equal(5.0, payload.BallData.Speed);
        Assert.Equal(-1.5, payload.BallData.HLA);
        Assert.Equal(0.0, payload.BallData.VLA);
    }

    [Fact]
    public void BuildPuttUsesExplicitSpinWhenGiven()
    {
        var payload = CreateCodec().BuildPutt(speed: 5.0, hla: 0.0, vla: 0.0, spin: 250.0);

        Assert.Equal(250.0, payload.BallData.BackSpin);
        Assert.Equal(250.0, payload.BallData.TotalSpin);
    }

    [Fact]
    public void BuildPuttSetsShapeAndShotOptions()
    {
        var payload = CreateCodec().BuildPutt(speed: 5.0, hla: 0.0, vla: 0.0);

        Assert.Equal("GolfSimVision", payload.DeviceID);
        Assert.Equal("Yards", payload.Units);
        Assert.Equal("1", payload.APIversion);
        Assert.True(payload.ShotDataOptions.ContainsBallData);
        Assert.False(payload.ShotDataOptions.ContainsClubData);
        Assert.False(payload.ShotDataOptions.IsHeartBeat);
    }

    [Fact]
    public void BuildPuttDrawsShotNumberFromProvidedCallable()
    {
        var codec = CreateCodec();
        var first = codec.BuildPutt(speed: 5.0, hla: 0.0, vla: 0.0);
        var second = codec.BuildPutt(speed: 5.0, hla: 0.0, vla: 0.0);

        Assert.Equal(100, first.ShotNumber);
        Assert.Equal(101, second.ShotNumber);
    }

    [Fact]
    public void BuildShotSetsFullBallData()
    {
        var payload = CreateCodec().BuildShot(speed: 150.0, hla: 1.0, vla: 15.0, totalSpin: 3000.0, spinAxis: -5.0);

        Assert.Equal(150.0, payload.BallData.Speed);
        Assert.Equal(3000.0, payload.BallData.TotalSpin);
        Assert.Equal(-5.0, payload.BallData.SpinAxis);
        Assert.Equal(3000.0, payload.BallData.BackSpin);
        Assert.Equal(0.0, payload.BallData.SideSpin);
    }

    [Fact]
    public void BuildShotAcceptsExplicitBackAndSideSpin()
    {
        var payload = CreateCodec().BuildShot(
            speed: 150.0, hla: 1.0, vla: 15.0, totalSpin: 3000.0, spinAxis: -5.0,
            backSpin: 2800.0, sideSpin: 400.0);

        Assert.Equal(2800.0, payload.BallData.BackSpin);
        Assert.Equal(400.0, payload.BallData.SideSpin);
    }
}
