using System.Collections.Generic;

namespace GolfSimVision.Calibration;

/// <summary>
/// The "manual known-distance entry" calibration method: the user identifies the 4
/// corners of a rectangular reference area on the mat, in order (top-left, top-right,
/// bottom-right, bottom-left), and enters its known real-world width/height. Builds the
/// same shape of point correspondences a printed-target method would otherwise supply
/// from known marker positions -- both methods feed the same HomographySolver.
/// </summary>
public static class RectangleCalibration
{
    public static IReadOnlyList<PointCorrespondence> BuildCorrespondences(
        PixelPoint topLeft,
        PixelPoint topRight,
        PixelPoint bottomRight,
        PixelPoint bottomLeft,
        double realWidthMm,
        double realHeightMm)
    {
        return new[]
        {
            new PointCorrespondence(topLeft, new RealWorldPoint(0, 0)),
            new PointCorrespondence(topRight, new RealWorldPoint(realWidthMm, 0)),
            new PointCorrespondence(bottomRight, new RealWorldPoint(realWidthMm, realHeightMm)),
            new PointCorrespondence(bottomLeft, new RealWorldPoint(0, realHeightMm)),
        };
    }
}
