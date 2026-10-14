using System;
using System.IO;
using GolfSimVision.Settings;
using GolfSimVision.Zones;
using Xunit;

namespace GolfSimVision.Tests.Settings;

public class SettingsStoreTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "GolfSimVisionTests_" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void LoadReturnsDefaultsWhenNoFileExists()
    {
        var settings = new SettingsStore(_tempDir).Load();

        Assert.Equal("127.0.0.1", settings.GsProHost);
        Assert.Equal(921, settings.GsProPort);
    }

    [Fact]
    public void SaveThenLoadRoundTrips()
    {
        var store = new SettingsStore(_tempDir);
        var settings = new AppSettings { GsProHost = "192.168.1.50", GsProPort = 921, DeviceId = "Test", Units = "Meters" };

        store.Save(settings);
        var reloaded = new SettingsStore(_tempDir).Load();

        Assert.Equal(settings, reloaded);
    }

    [Fact]
    public void SaveThenLoadRoundTripsANonDefaultZoneConfig()
    {
        var store = new SettingsStore(_tempDir);
        var zone = new ZoneConfig(new StartZone(0.12, 0.2, 0.3, 0.4), 0.7);
        var settings = new AppSettings { Zone = zone };

        store.Save(settings);
        var reloaded = new SettingsStore(_tempDir).Load();

        Assert.Equal(zone, reloaded.Zone);
    }

    [Fact]
    public void DefaultLocationIsUnderLocalApplicationData()
    {
        var store = new SettingsStore();
        var expectedRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(expectedRoot, store.FilePath);
    }
}
