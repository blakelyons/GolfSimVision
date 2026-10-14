using GolfSimVision.Detection;
using GolfSimVision.Zones;

namespace GolfSimVision.UiMessages;

/// <summary>
/// Typed outbound message to the WebView2 UI. Serialized as-is (PascalCase, matching
/// these property names directly) -- the JS side reads the same field names rather than
/// the backend applying a naming-policy translation. One flat status record for the
/// whole app for now; split into per-subsystem messages if this grows unwieldy.
/// </summary>
public sealed record AppStatusMessage(
    string ConnectionState,
    bool IsReadyForNextShot,
    int? LastResponseCode,
    string? LastResponseMessage,
    string? LastError,
    string? CaptureMode = null,
    string? RingBufferState = null,
    double? MeasuredFps = null,
    bool CaptureDegraded = false,
    string? CaptureDegradedReason = null,
    string? CameraError = null,
    int? LastStrokeFrameCount = null,
    ZoneConfig? Zone = null,
    double? CalibrationMillimetersPerPixel = null,
    int? CalibrationVersion = null,
    string? CalibrationError = null,
    string? DetectorError = null,
    PuttMeasurement? LastPutt = null,
    string? LastPuttRejectionReason = null,
    string? LastPuttSendError = null);
