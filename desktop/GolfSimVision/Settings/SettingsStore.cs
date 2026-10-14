using System;
using System.IO;
using System.Text.Json;

namespace GolfSimVision.Settings;

/// <summary>
/// Persists AppSettings as JSON outside any watched/reloaded source tree (ADR-0006
/// non-negotiable: a settings write must never be mistaken for a source change and
/// trigger a restart). Defaults to the per-user LocalApplicationData folder, never a
/// path under the app's own install/build directory.
/// </summary>
public sealed class SettingsStore
{
    public SettingsStore(string? directoryOverride = null)
    {
        var directory = directoryOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GolfSimVision");
        Directory.CreateDirectory(directory);
        FilePath = Path.Combine(directory, "settings.json");
    }

    public string FilePath { get; }

    public AppSettings Load()
    {
        if (!File.Exists(FilePath)) return new AppSettings();
        var json = File.ReadAllText(FilePath);
        return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
