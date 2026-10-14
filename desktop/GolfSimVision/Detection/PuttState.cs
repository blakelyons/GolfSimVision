namespace GolfSimVision.Detection;

/// <summary>
/// Ported from zone_state_machine.py's PuttState. Complete is declared but never
/// actually entered in the Python original -- Finalize() always returns straight to
/// Idle, whether the track was accepted or rejected. Kept exactly as-is (an unmodified
/// port), not "fixed," since this is the file ticket #6 names as the reference.
/// </summary>
public enum PuttState
{
    Idle,
    Waiting,
    Tracking,
    Complete,
}
