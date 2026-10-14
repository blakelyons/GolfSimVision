using System;
using System.Collections.Generic;
using System.Linq;
using GolfSimVision.Calibration;
using Xunit;

namespace GolfSimVision.Tests.Calibration;

public class HomographySolverTests
{
    [Fact]
    public void ThrowsWithFewerThanFourCorrespondences()
    {
        var points = new[]
        {
            new PointCorrespondence(new PixelPoint(0, 0), new RealWorldPoint(0, 0)),
            new PointCorrespondence(new PixelPoint(1, 0), new RealWorldPoint(10, 0)),
            new PointCorrespondence(new PixelPoint(0, 1), new RealWorldPoint(0, 10)),
        };

        Assert.Throws<ArgumentException>(() => HomographySolver.Solve(points));
    }

    [Fact]
    public void RecoversAKnownPerspectiveTransformAndReprojectsAHeldOutPoint()
    {
        // A genuine perspective transform (nonzero bottom-left/middle terms), not merely
        // affine -- proves the solver fits a true homography, not just a simpler model.
        var groundTruth = new HomographyMatrix(new[]
        {
            1.5, 0.2, 50.0,
            -0.1, 1.8, 30.0,
            0.0004, 0.0006, 1.0,
        });

        // 6 points to solve from, spread across the frame (not collinear); a 7th held
        // out entirely from fitting, used only to check the recovered transform
        // generalizes rather than having merely memorized its inputs.
        var pixelPoints = new[]
        {
            new PixelPoint(0, 0), new PixelPoint(200, 0), new PixelPoint(200, 150),
            new PixelPoint(0, 150), new PixelPoint(100, 75), new PixelPoint(50, 120),
        };
        var heldOutPixel = new PixelPoint(150, 30);

        var correspondences = pixelPoints
            .Select(p => new PointCorrespondence(p, groundTruth.MapPixelToReal(p)))
            .ToList();

        var recovered = HomographySolver.Solve(correspondences);

        foreach (var c in correspondences)
        {
            var reprojected = recovered.MapPixelToReal(c.Pixel);
            AssertClose(c.RealWorld, reprojected);
        }

        var expectedHeldOut = groundTruth.MapPixelToReal(heldOutPixel);
        var actualHeldOut = recovered.MapPixelToReal(heldOutPixel);
        AssertClose(expectedHeldOut, actualHeldOut);
    }

    [Fact]
    public void RecoversASimpleScaleAndTranslation()
    {
        // No perspective term at all -- the simplest case, and a sanity cross-check that
        // the recovered mm/px-style scale is right when there's no perspective to distort it.
        var groundTruth = new HomographyMatrix(new double[] { 4, 0, 100, 0, 4, 200, 0, 0, 1 });
        var pixelPoints = new[]
        {
            new PixelPoint(0, 0), new PixelPoint(50, 0), new PixelPoint(50, 50), new PixelPoint(0, 50),
        };
        var correspondences = pixelPoints
            .Select(p => new PointCorrespondence(p, groundTruth.MapPixelToReal(p)))
            .ToList();

        var recovered = HomographySolver.Solve(correspondences);

        var scale = CalibrationService.EstimateScale(correspondences);
        Assert.Equal(4.0, scale, precision: 6);

        var probe = recovered.MapPixelToReal(new PixelPoint(25, 25));
        AssertClose(groundTruth.MapPixelToReal(new PixelPoint(25, 25)), probe);
    }

    private static void AssertClose(RealWorldPoint expected, RealWorldPoint actual, double tolerance = 1e-3)
    {
        Assert.True(
            Math.Abs(expected.Xmm - actual.Xmm) < tolerance && Math.Abs(expected.Ymm - actual.Ymm) < tolerance,
            $"Expected ({expected.Xmm:F6}, {expected.Ymm:F6}) but got ({actual.Xmm:F6}, {actual.Ymm:F6})");
    }
}
