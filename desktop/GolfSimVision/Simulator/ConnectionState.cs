namespace GolfSimVision.Simulator;

/// <summary>
/// Ported from protocol.py's ConnectionState, preserving the same vocabulary decision
/// recorded in prototypes/gspro-harness/SPEC.md: Reconnecting (not "RECONNECT_BACKOFF"),
/// distinct from Connecting -- Connecting means "never yet connected" (e.g. wrong port),
/// Reconnecting means "worked once, then dropped."
/// </summary>
public enum ConnectionState
{
    Disabled,
    Connecting,
    Connected,
    Reconnecting,
    Stopped,
}
