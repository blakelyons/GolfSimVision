using System;
using System.Collections.Generic;
using System.Text.Json;

namespace GolfSimVision.Simulator;

/// <summary>
/// Wire-format serialization and framing, ported directly from
/// prototypes/gspro-harness/protocol.py's serialize_payload/parse_response/
/// find_json_end.
/// </summary>
public static class JsonFraming
{
    // Real GSPro frames are a few hundred bytes. If the undrained inbound buffer ever
    // passes this with no complete frame, the caller should resync by dropping it
    // rather than growing memory without limit.
    public const int MaxFrameBytes = 64 * 1024;

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        WriteIndented = false,
    };

    public static byte[] SerializePayload(ShotPayload payload)
        => JsonSerializer.SerializeToUtf8Bytes(payload, SerializeOptions);

    /// <summary>Parses a GSPro reply. Throws FormatException on malformed JSON.</summary>
    public static GsProResponse ParseResponse(byte[] raw)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(raw);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"Malformed GSPro response: {ex.Message}", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new FormatException("Malformed GSPro response: not a JSON object");

            var code = root.TryGetProperty("Code", out var codeEl) ? codeEl.GetInt32() : 0;
            var message = root.TryGetProperty("Message", out var msgEl) ? msgEl.GetString() ?? "" : "";

            // Player's real shape isn't fully known (GSPRO_OPENCONNECT.md only confirms a
            // "Handed" field) -- a typed dictionary of cloned JsonElements avoids both an
            // untyped mutable JSON DOM node and inventing an unverified full schema.
            IReadOnlyDictionary<string, JsonElement>? player = null;
            if (root.TryGetProperty("Player", out var playerEl) && playerEl.ValueKind == JsonValueKind.Object)
            {
                var dict = new Dictionary<string, JsonElement>();
                foreach (var prop in playerEl.EnumerateObject())
                {
                    dict[prop.Name] = prop.Value.Clone();
                }
                player = dict;
            }

            return new GsProResponse(code, message, player);
        }
    }

    /// <summary>
    /// Index past the first complete top-level JSON object in buf, or null.
    ///
    /// GSPro neither length-prefixes nor delimits frames, so framing is on balanced
    /// top-level braces, string-aware so quoted braces don't affect depth. Ported
    /// directly from protocol.py's find_json_end -- a byte outside the ASCII range
    /// (protocol.py's `chr(b) if b < 128 else ""`) is equally a no-op here, since
    /// casting it to char can never equal '"', '\\', '{' or '}'.
    /// </summary>
    public static int? FindJsonEnd(byte[] buf)
    {
        var depth = 0;
        var inString = false;
        var escape = false;
        var started = false;

        for (var i = 0; i < buf.Length; i++)
        {
            var ch = (char)buf[i];
            if (inString)
            {
                if (escape)
                {
                    escape = false;
                }
                else if (ch == '\\')
                {
                    escape = true;
                }
                else if (ch == '"')
                {
                    inString = false;
                }
            }
            else
            {
                if (ch == '"')
                {
                    inString = true;
                }
                else if (ch == '{')
                {
                    depth++;
                    started = true;
                }
                else if (ch == '}')
                {
                    depth--;
                    if (started && depth == 0) return i + 1;
                }
            }
        }

        return null;
    }
}
