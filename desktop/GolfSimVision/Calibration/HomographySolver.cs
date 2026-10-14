using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;

namespace GolfSimVision.Calibration;

/// <summary>
/// The only class in this module that touches OpenCvSharp. Computes a homography from
/// point correspondences via Cv2.FindHomography (DLT least-squares), then immediately
/// converts the result to a plain HomographyMatrix -- deliberately not reimplementing
/// the underlying linear algebra by hand, the same reasoning that keeps the rest of this
/// project on OpenCvSharp/OpenCV rather than hand-rolled CV math.
/// </summary>
public static class HomographySolver
{
    public static HomographyMatrix Solve(IReadOnlyList<PointCorrespondence> correspondences)
    {
        if (correspondences.Count < 4)
        {
            throw new ArgumentException(
                "A homography needs at least 4 point correspondences.", nameof(correspondences));
        }

        var src = correspondences.Select(c => new Point2d(c.Pixel.X, c.Pixel.Y)).ToArray();
        var dst = correspondences.Select(c => new Point2d(c.RealWorld.Xmm, c.RealWorld.Ymm)).ToArray();

        using var h = Cv2.FindHomography(src, dst);
        if (h.Empty() || h.Rows != 3 || h.Cols != 3)
        {
            throw new InvalidOperationException(
                "FindHomography failed to produce a valid 3x3 matrix -- check for degenerate (collinear or duplicate) points.");
        }

        var values = new double[9];
        for (var r = 0; r < 3; r++)
        {
            for (var c = 0; c < 3; c++)
            {
                values[r * 3 + c] = h.At<double>(r, c);
            }
        }

        return new HomographyMatrix(values);
    }
}
