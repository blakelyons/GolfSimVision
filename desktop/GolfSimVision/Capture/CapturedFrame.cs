namespace GolfSimVision.Capture;

/// <summary>
/// One captured frame from the measurement stream. Raw BGR bytes, not an encoded
/// image -- this is the high-rate stream a detector (a later ticket) will consume, kept
/// deliberately separate from the lower-rate, best-effort JPEG preview stream (ADR-0003:
/// "Smooth preview and dense measurement sampling are conflicting requirements on one
/// stream. They are two streams.").
/// </summary>
public sealed record CapturedFrame(double TimestampSeconds, int Width, int Height, byte[] BgrBytes);
