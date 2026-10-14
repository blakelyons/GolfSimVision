namespace GolfSimVision.LaunchMonitor;

/// <summary>
/// Typed outbound E6 Connect messages. Ported from V1's backend/monitors/r10.py's dict
/// literals -- same shapes, now typed (ADR-0006) rather than ad hoc dicts.
/// </summary>
public sealed record HandshakeResponse
{
    public string Type { get; } = "Handshake";
    public required string Challenge { get; init; }
    public string E6Version { get; init; } = "2, 0, 0, 0";
    public string ProtocolVersion { get; init; } = "1.0.0.5";
    public string RequiredProtocolVersion { get; init; } = "1.0.0.0";
}

public sealed record AuthenticationResponse
{
    public string Type { get; } = "Authentication";
    public string Success { get; } = "true";
}

public sealed record AckResponse
{
    public string Type { get; } = "ACK";
    public required string SubType { get; init; }
    public string Details { get; init; } = "Success.";
}

public sealed record SimCommand
{
    public string Type { get; } = "SimCommand";
    public required string SubType { get; init; }
}
