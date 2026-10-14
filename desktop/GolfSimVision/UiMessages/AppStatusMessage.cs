namespace GolfSimVision.UiMessages;

/// <summary>
/// Typed outbound message to the WebView2 UI. Serialized as-is (PascalCase, matching
/// these property names directly) -- the JS side reads the same field names rather than
/// the backend applying a naming-policy translation.
/// </summary>
public sealed record AppStatusMessage(
    string ConnectionState,
    bool IsReadyForNextShot,
    int? LastResponseCode,
    string? LastResponseMessage,
    string? LastError);
