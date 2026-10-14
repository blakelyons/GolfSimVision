namespace GolfSimVision.Settings;

/// <summary>Persisted app configuration. Grows as later tickets add zone/gateway and
/// calibration settings.</summary>
public sealed record AppSettings
{
    public string GsProHost { get; init; } = "127.0.0.1";
    public int GsProPort { get; init; } = 921;
    public string DeviceId { get; init; } = "GolfSimVision";
    public string Units { get; init; } = "Yards";

    // Camera defaults match CAMERA_CAPABILITIES.md's confirmed real ceiling for the
    // reference hardware (720p60 via CAP_MSMF) -- not the originally-assumed 720p120.
    public int CameraIndex { get; init; }
    public int CameraWidth { get; init; } = 1280;
    public int CameraHeight { get; init; } = 720;
    public int CameraFps { get; init; } = 60;

    /// Null means "don't touch exposure" -- unset until a real calibration/setup flow
    /// (a later ticket) determines a locked value for the user's actual hardware.
    public double? CameraExposure { get; init; }
    public double? CameraGain { get; init; }

    /// Frames of pre-roll kept while the ring buffer is armed (Putting mode).
    public int RingBufferCapacity { get; init; } = 240;
}
