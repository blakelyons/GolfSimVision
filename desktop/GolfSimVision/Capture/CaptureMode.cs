namespace GolfSimVision.Capture;

/// <summary>ADR-0003's two top-level modes. The session stays alive continuously in
/// both -- only the expensive parts (ring buffer) are gated by mode.</summary>
public enum CaptureMode
{
    FullShot,
    Putting,
}

/// <summary>Sub-state of Putting mode's ring buffer. StrokeDetected in ADR-0003's table
/// corresponds to Shipping here.</summary>
public enum RingBufferState
{
    Off,
    Armed,
    Shipping,
}
