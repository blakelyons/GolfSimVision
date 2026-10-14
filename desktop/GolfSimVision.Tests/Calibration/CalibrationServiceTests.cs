using System;
using System.IO;
using GolfSimVision.Calibration;
using Xunit;

namespace GolfSimVision.Tests.Calibration;

public class CalibrationServiceTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "GolfSimVisionCalServiceTests_" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private static PointCorrespondence[] SquareCorrespondences() => new[]
    {
        new PointCorrespondence(new PixelPoint(0, 0), new RealWorldPoint(0, 0)),
        new PointCorrespondence(new PixelPoint(100, 0), new RealWorldPoint(200, 0)),
        new PointCorrespondence(new PixelPoint(100, 100), new RealWorldPoint(200, 200)),
        new PointCorrespondence(new PixelPoint(0, 100), new RealWorldPoint(0, 200)),
    };

    [Fact]
    public void CurrentIsNullBeforeAnyCalibration()
    {
        var service = new CalibrationService(new CalibrationStore(_tempDir));
        Assert.Null(service.Current);
    }

    [Fact]
    public void CalibrateComputesScaleAndPersists()
    {
        var store = new CalibrationStore(_tempDir);
        var service = new CalibrationService(store);

        var data = service.Calibrate(SquareCorrespondences());

        Assert.Equal(2.0, data.MillimetersPerPixel, precision: 6); // 200mm over 100px
        Assert.Equal(1, data.Version);
        Assert.Same(data, service.Current);
        Assert.Equal(data, store.Load());
    }

    [Fact]
    public void VersionIncrementsAcrossSuccessiveCalibrations()
    {
        var service = new CalibrationService(new CalibrationStore(_tempDir));

        var first = service.Calibrate(SquareCorrespondences());
        var second = service.Calibrate(SquareCorrespondences());

        Assert.Equal(1, first.Version);
        Assert.Equal(2, second.Version);
    }

    [Fact]
    public void LoadsThePreviouslyPersistedCalibrationOnConstruction()
    {
        var store = new CalibrationStore(_tempDir);
        new CalibrationService(store).Calibrate(SquareCorrespondences());

        // A fresh service instance (e.g. the next app launch) should pick up what was
        // already on disk, not start from scratch.
        var reopened = new CalibrationService(store);

        Assert.NotNull(reopened.Current);
        Assert.Equal(1, reopened.Current!.Version);
    }

    [Fact]
    public void EstimateScaleAveragesAcrossAllPointPairs()
    {
        // Three collinear points at 1:2 real:pixel scale -- every pairwise ratio should
        // agree, so the average should too.
        var points = new[]
        {
            new PointCorrespondence(new PixelPoint(0, 0), new RealWorldPoint(0, 0)),
            new PointCorrespondence(new PixelPoint(10, 0), new RealWorldPoint(20, 0)),
            new PointCorrespondence(new PixelPoint(20, 0), new RealWorldPoint(40, 0)),
        };

        var scale = CalibrationService.EstimateScale(points);

        Assert.Equal(2.0, scale, precision: 9);
    }
}
