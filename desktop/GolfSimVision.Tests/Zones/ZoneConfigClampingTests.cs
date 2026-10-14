using GolfSimVision.Zones;
using Xunit;

namespace GolfSimVision.Tests.Zones;

public class ZoneConfigClampingTests
{
    [Fact]
    public void LeavesAnAlreadyValidConfigUnchanged()
    {
        var config = new ZoneConfig(new StartZone(0.1, 0.2, 0.3, 0.4), 0.6);

        var clamped = ZoneConfigClamping.Clamp(config);

        Assert.Equal(config, clamped);
    }

    [Fact]
    public void EnforcesAMinimumZoneSize()
    {
        var config = new ZoneConfig(new StartZone(0.1, 0.1, 0.001, 0.001), 0.5);

        var clamped = ZoneConfigClamping.Clamp(config);

        Assert.Equal(0.05, clamped.StartZone.W);
        Assert.Equal(0.05, clamped.StartZone.H);
    }

    [Theory]
    [InlineData(-0.5, 0.1)]
    [InlineData(0.0, 0.1)]
    [InlineData(1.5, 0.9)]
    [InlineData(1.0, 0.9)]
    public void KeepsGatewayXWithinBounds(double input, double expected)
    {
        var config = new ZoneConfig(StartZone.Default, input);

        var clamped = ZoneConfigClamping.Clamp(config);

        Assert.Equal(expected, clamped.GatewayX);
    }
}
