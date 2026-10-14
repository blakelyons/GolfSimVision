namespace GolfSimVision.UiMessages;

/// <summary>
/// Typed inbound message from the WebView2 UI (ADR-0006: typed messages on every
/// interface, no untyped dict/object payloads). "sendTestShot" is the only variant this
/// ticket needs; later tickets add more Types as the UI grows (e.g. zone/gateway drag
/// updates, calibration commands).
/// </summary>
public sealed record UiRequest(string Type);
