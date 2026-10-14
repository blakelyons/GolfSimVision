using System;
using System.Collections.Generic;
using System.Linq;

namespace GolfSimVision.Calibration;

/// <summary>The ticket's named interface: exposes the current scale/homography for the
/// detection pipeline (a later ticket) to consume. Nullable -- a fresh install has no
/// calibration yet.</summary>
public interface ICalibrationProvider
{
    CalibrationData? Current { get; }
}

/// <summary>
/// Orchestrates calibration: solves the homography, estimates a representative mm/px
/// scale, persists it versioned, and exposes it via ICalibrationProvider. One entry
/// point (Calibrate) serves both calibration methods -- RectangleCalibration and a
/// future printed-target method both just build correspondences differently before
/// calling it.
/// </summary>
public sealed class CalibrationService : ICalibrationProvider
{
    private readonly CalibrationStore _store;

    public CalibrationService(CalibrationStore store)
    {
        _store = store;
        Current = _store.Load();
    }

    public CalibrationData? Current { get; private set; }

    public CalibrationData Calibrate(IReadOnlyList<PointCorrespondence> correspondences)
    {
        var homography = HomographySolver.Solve(correspondences);
        var scale = EstimateScale(correspondences);
        var version = (Current?.Version ?? 0) + 1;
        var data = new CalibrationData(homography, scale, version, DateTimeOffset.UtcNow);

        _store.Save(data);
        Current = data;
        return data;
    }

    /// Representative mm/px scale, averaged across every pair of correspondence points.
    /// A cross-check value (ADR-0005: "Ball radius is demoted to a cross-check, not the
    /// ruler") -- never the primary transform; that's the homography itself.
    public static double EstimateScale(IReadOnlyList<PointCorrespondence> points)
    {
        var ratios = new List<double>();
        for (var i = 0; i < points.Count; i++)
        {
            for (var j = i + 1; j < points.Count; j++)
            {
                var pixelDist = Distance(points[i].Pixel.X, points[i].Pixel.Y, points[j].Pixel.X, points[j].Pixel.Y);
                var realDist = Distance(points[i].RealWorld.Xmm, points[i].RealWorld.Ymm, points[j].RealWorld.Xmm, points[j].RealWorld.Ymm);
                if (pixelDist > 0) ratios.Add(realDist / pixelDist);
            }
        }
        return ratios.Count > 0 ? ratios.Average() : 0.0;
    }

    private static double Distance(double x1, double y1, double x2, double y2)
        => Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
}
