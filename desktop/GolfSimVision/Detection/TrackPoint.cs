namespace GolfSimVision.Detection;

/// <summary>One recorded position during TRACKING. X/Y normalized 0-1; TimestampSeconds
/// is wall-clock (monotonic-equivalent), matching zone_state_machine.py's TrackPoint.
/// </summary>
public sealed record TrackPoint(double X, double Y, double PixelsPerMm, double TimestampSeconds);
