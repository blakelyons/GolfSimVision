namespace GolfSimVision.Simulator;

/// <summary>
/// Wire-format records for GSPro's OpenConnect protocol. Field names and casing match
/// the JSON GSPro expects exactly (including "DeviceID" and "APIversion") -- these are
/// not C# naming convention choices, they are the wire contract.
///
/// Ported from prototypes/gspro-harness/protocol.py's dataclasses. See
/// docs/GSPRO_OPENCONNECT.md and docs/GSPRO_PUTTING.md for the verified facts these
/// shapes encode.
/// </summary>
public sealed record BallData
{
    public double Speed { get; init; }
    public double SpinAxis { get; init; }
    public double TotalSpin { get; init; }
    public double BackSpin { get; init; }
    public double SideSpin { get; init; }
    public double HLA { get; init; }
    public double VLA { get; init; }
    public double CarryDistance { get; init; }
}

public sealed record ClubData
{
    public double Speed { get; init; }
    public double AngleOfAttack { get; init; }
    public double FaceToTarget { get; init; }
    public double Lie { get; init; }
    public double Loft { get; init; }
    public double Path { get; init; }
    public double SpeedAtImpact { get; init; }
    public double VerticalFaceImpact { get; init; }
    public double HorizontalFaceImpact { get; init; }
    public double ClosureRate { get; init; }
}

/// <summary>
/// Per GSPRO_OPENCONNECT.md's ContainsBallData finding (exp 20): GSPro honors this flag,
/// not whether BallData is actually populated. A caller that sets ContainsBallData=false
/// with a populated BallData gets silently dropped -- no error, no response at all.
/// </summary>
public sealed record ShotDataOptions
{
    public bool ContainsBallData { get; init; } = true;
    public bool ContainsClubData { get; init; }
    public bool LaunchMonitorIsReady { get; init; } = true;
    public bool LaunchMonitorBallDetected { get; init; } = true;
    public bool IsHeartBeat { get; init; }
}

/// <summary>
/// Every field is always present on the wire, even for a heartbeat -- BallData/ClubData
/// default to their zero values rather than being omitted, matching protocol.py's
/// asdict() behavior exactly (GSPro has never been observed to care, but this keeps the
/// wire shape identical to what Phase 0 already verified GSPro accepts).
/// </summary>
public sealed record ShotPayload
{
    public required string DeviceID { get; init; }
    public required string Units { get; init; }
    public required int ShotNumber { get; init; }

    /// Literal string "1", not a number -- GSPro's spec requires a string here. Both
    /// "APIversion" and "APIVersion" casing are accepted on receipt (exp 14), but this
    /// is the spec-correct key name to send.
    public required string APIversion { get; init; }

    public BallData BallData { get; init; } = new();
    public ClubData ClubData { get; init; } = new();
    public ShotDataOptions ShotDataOptions { get; init; } = new();
}

/// <summary>
/// An inbound frame from GSPro. Known codes (GSPRO_OPENCONNECT.md): 200 "Ball Data
/// received", 201 "GSPro Player Information" (fires twice per club change), 202 "GSPro
/// ready" (the actual readiness signal -- see SimulatorConnector.IsReadyForNextShot),
/// 203 "GSPro round ended" (benign, unexplained), 501 "Bad format". GSPro gives no
/// field correlating a response to a specific request.
/// </summary>
public sealed record GsProResponse(int Code, string Message = "", System.Text.Json.Nodes.JsonObject? Player = null);
