namespace GolfSimVision.Calibration;

public sealed record PixelPoint(double X, double Y);

public sealed record RealWorldPoint(double Xmm, double Ymm);

/// <summary>One known pairing of a pixel location to its real-world position (mm) on the
/// putting surface -- the raw input a homography is solved from.</summary>
public sealed record PointCorrespondence(PixelPoint Pixel, RealWorldPoint RealWorld);
