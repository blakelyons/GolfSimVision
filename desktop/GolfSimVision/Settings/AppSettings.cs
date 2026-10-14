namespace GolfSimVision.Settings;

/// <summary>Persisted app configuration. Grows as later tickets add camera/zone/
/// calibration settings; this ticket only needs the GSPro connection fields.</summary>
public sealed record AppSettings
{
    public string GsProHost { get; init; } = "127.0.0.1";
    public int GsProPort { get; init; } = 921;
    public string DeviceId { get; init; } = "GolfSimVision";
    public string Units { get; init; } = "Yards";
}
