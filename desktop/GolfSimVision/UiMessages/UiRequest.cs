namespace GolfSimVision.UiMessages;

/// <summary>
/// Typed inbound message from the WebView2 UI (ADR-0006: typed messages on every
/// interface, no untyped dict/object payloads). Variants so far: "sendTestShot",
/// "setCaptureMode" (Mode: "fullShot" | "putting"), "triggerTestStroke",
/// "endTestStroke". Mode is only meaningful for setCaptureMode. A small flat record
/// rather than a polymorphic JSON hierarchy -- proportionate to four variants; revisit
/// if this grows much further.
/// </summary>
public sealed record UiRequest(string Type, string? Mode = null);
