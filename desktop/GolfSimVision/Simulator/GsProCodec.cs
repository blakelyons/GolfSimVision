using System;

namespace GolfSimVision.Simulator;

public static class PuttSpin
{
    /// Rough putt backspin estimate, used when a putt's spin isn't given explicitly.
    /// Ported from protocol.py's rolling_spin_rpm; see GSPRO_PUTTING.md exp 10.
    public static double RollingSpinRpm(double speedMph) => 200.0 * speedMph;
}

/// <summary>
/// Builds ShotPayloads for putts and full shots. Stateless wire-format layer -- given
/// inputs, returns a ShotPayload. ShotNumber values are pulled from the supplied
/// nextShotNumber callback (normally SimulatorConnector.NextShotNumber), so every caller
/// building payloads through the same codec shares one counter rather than drifting out
/// of sync. Ported from protocol.py's GSProCodec.
/// </summary>
public sealed class GsProCodec
{
    private readonly string _deviceId;
    private readonly string _units;
    private readonly Func<int> _nextShotNumber;

    public GsProCodec(string deviceId, Func<int> nextShotNumber, string units = "Yards")
    {
        _deviceId = deviceId;
        _units = units;
        _nextShotNumber = nextShotNumber;
    }

    public ShotPayload BuildPutt(double speed, double hla, double vla, double? spin = null)
    {
        var backSpin = spin ?? PuttSpin.RollingSpinRpm(speed);
        return new ShotPayload
        {
            DeviceID = _deviceId,
            Units = _units,
            ShotNumber = _nextShotNumber(),
            APIversion = "1",
            BallData = new BallData
            {
                Speed = speed,
                HLA = hla,
                VLA = vla,
                BackSpin = backSpin,
                TotalSpin = backSpin,
            },
            ShotDataOptions = new ShotDataOptions(),
        };
    }

    public ShotPayload BuildShot(
        double speed,
        double hla,
        double vla,
        double totalSpin,
        double spinAxis,
        double? backSpin = null,
        double? sideSpin = null)
    {
        return new ShotPayload
        {
            DeviceID = _deviceId,
            Units = _units,
            ShotNumber = _nextShotNumber(),
            APIversion = "1",
            BallData = new BallData
            {
                Speed = speed,
                HLA = hla,
                VLA = vla,
                TotalSpin = totalSpin,
                SpinAxis = spinAxis,
                BackSpin = backSpin ?? totalSpin,
                SideSpin = sideSpin ?? 0.0,
            },
            ShotDataOptions = new ShotDataOptions(),
        };
    }
}
