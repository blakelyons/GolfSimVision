using System;
using System.IO;
using GolfSimVision.Calibration;
using Xunit;

namespace GolfSimVision.Tests.Calibration;

public class CalibrationStoreTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "GolfSimVisionCalTests_" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void LoadReturnsNullWhenNoFileExists()
    {
        var store = new CalibrationStore(_tempDir);
        Assert.Null(store.Load());
    }

    [Fact]
    public void SaveThenLoadRoundTrips()
    {
        var store = new CalibrationStore(_tempDir);
        var data = new CalibrationData(
            new HomographyMatrix(new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }),
            MillimetersPerPixel: 0.75,
            Version: 3,
            CalibratedAtUtc: DateTimeOffset.UtcNow);

        store.Save(data);
        var reloaded = new CalibrationStore(_tempDir).Load();

        Assert.Equal(data, reloaded);
    }
}
