using GolfSimVision.Calibration;
using GolfSimVision.Zones;

namespace GolfSimVision.UiMessages;

/// <summary>
/// Typed inbound message from the WebView2 UI (ADR-0006: typed messages on every
/// interface, no untyped dict/object payloads). Variants so far: "sendTestShot",
/// "setCaptureMode" (Mode), "triggerTestStroke", "endTestStroke", "updateZoneConfig"
/// (Zone, sent once when a drag completes), "calibrate" (Corners + RealWidthMm +
/// RealHeightMm -- the manual-rectangle calibration method). This flat record is
/// growing; a polymorphic per-type payload would be more honest past this point, but is
/// more ceremony than six variants justify yet.
/// </summary>
public sealed record UiRequest(
    string Type,
    string? Mode = null,
    ZoneConfig? Zone = null,
    PixelPoint[]? Corners = null,
    double? RealWidthMm = null,
    double? RealHeightMm = null);
