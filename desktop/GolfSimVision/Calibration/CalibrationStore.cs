using System;
using System.IO;
using System.Text.Json;

namespace GolfSimVision.Calibration;

/// <summary>
/// Persists CalibrationData as JSON outside any watched/reloaded source tree -- same
/// discipline as SettingsStore. A separate file, not folded into AppSettings: it's a
/// distinct, versioned artifact with its own lifecycle (recalibrating doesn't touch any
/// other setting, and nothing else should bump its version).
/// </summary>
public sealed class CalibrationStore
{
    public CalibrationStore(string? directoryOverride = null)
    {
        var directory = directoryOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GolfSimVision");
        Directory.CreateDirectory(directory);
        FilePath = Path.Combine(directory, "calibration.json");
    }

    public string FilePath { get; }

    public CalibrationData? Load()
    {
        if (!File.Exists(FilePath)) return null;
        var json = File.ReadAllText(FilePath);
        return JsonSerializer.Deserialize<CalibrationData>(json);
    }

    public void Save(CalibrationData data)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
