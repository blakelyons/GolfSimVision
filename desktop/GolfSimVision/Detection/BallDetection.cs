namespace GolfSimVision.Detection;

/// <summary>
/// A single ball detection from one frame. Ported from prototypes/webcam-putting/
/// detection/detector.py's Detection dataclass -- X/Y/W/H normalized 0-1 relative to the
/// full frame, PixelsPerMm derived from the bounding box width (a per-detection
/// cross-check value; calibration's homography is the authoritative scale once a putt
/// is actually measured). Named BallDetection, not Detection, to avoid colliding with
/// this module's own namespace (GolfSimVision.Detection) when referenced unqualified
/// from a sibling test namespace of the same leaf name.
/// </summary>
public sealed record BallDetection(double X, double Y, double W, double H, double Confidence, double PixelsPerMm);
