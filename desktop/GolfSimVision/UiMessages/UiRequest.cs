using GolfSimVision.Zones;

namespace GolfSimVision.UiMessages;

/// <summary>
/// Typed inbound message from the WebView2 UI (ADR-0006: typed messages on every
/// interface, no untyped dict/object payloads). Variants so far: "sendTestShot",
/// "setCaptureMode" (Mode: "fullShot" | "putting"), "triggerTestStroke",
/// "endTestStroke", "updateZoneConfig" (Zone: the full new config, sent once when a drag
/// completes -- not on every mousemove). A small flat record rather than a polymorphic
/// JSON hierarchy -- proportionate to five variants; revisit if this grows much further.
/// </summary>
public sealed record UiRequest(string Type, string? Mode = null, ZoneConfig? Zone = null);
