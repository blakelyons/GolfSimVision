namespace GolfSimVision.Zones;

/// <summary>
/// Normalized (0-1, relative to frame) start-zone rectangle -- the shape
/// zone_state_machine.py's ZoneStateMachine.start_zone already expects, ported
/// unmodified from prototypes/webcam-putting/detection/zone_state_machine.py. Camera is
/// overhead, ball travels left -> right (increasing X).
/// </summary>
public sealed record StartZone(double X, double Y, double W, double H)
{
    public static StartZone Default { get; } = new(0.05, 0.1, 0.25, 0.8);
}

/// <summary>
/// The configuration surface the detection pipeline (a later ticket) consumes: where
/// the ball rests (StartZone) and where a stroke should be recognized (GatewayX, a
/// vertical threshold the ball must cross rightward). Matches zone_state_machine.py's
/// start_zone/gateway_x fields and defaults exactly.
/// </summary>
public sealed record ZoneConfig(StartZone StartZone, double GatewayX)
{
    public static ZoneConfig Default { get; } = new(StartZone.Default, 0.55);
}
