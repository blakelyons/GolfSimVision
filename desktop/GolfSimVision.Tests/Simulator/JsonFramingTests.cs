using System;
using System.Text;
using System.Text.Json;
using GolfSimVision.Simulator;
using Xunit;

namespace GolfSimVision.Tests.Simulator;

/// Mirrors prototypes/gspro-harness/test_protocol.py's TestSerializePayload.
public class SerializePayloadTests
{
    [Fact]
    public void RoundTripShape()
    {
        var payload = new ShotPayload
        {
            DeviceID = "GolfSimVision",
            Units = "Yards",
            ShotNumber = 1,
            APIversion = "1",
            BallData = new BallData { Speed = 5.0, HLA = -1.5, VLA = 0.0 },
            ShotDataOptions = new ShotDataOptions
            {
                ContainsBallData = true,
                ContainsClubData = false,
                LaunchMonitorIsReady = true,
                LaunchMonitorBallDetected = true,
                IsHeartBeat = false,
            },
        };

        var raw = JsonFraming.SerializePayload(payload);
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        Assert.Equal("GolfSimVision", root.GetProperty("DeviceID").GetString());
        Assert.Equal("1", root.GetProperty("APIversion").GetString());
        Assert.Equal(JsonValueKind.String, root.GetProperty("APIversion").ValueKind);
        Assert.Equal(5.0, root.GetProperty("BallData").GetProperty("Speed").GetDouble());
        Assert.True(root.GetProperty("ShotDataOptions").GetProperty("LaunchMonitorBallDetected").GetBoolean());
    }

    [Fact]
    public void NoTrailingNewlineAndCompactSeparators()
    {
        var payload = new ShotPayload { DeviceID = "d", Units = "Yards", ShotNumber = 1, APIversion = "1" };
        var raw = JsonFraming.SerializePayload(payload);
        var text = Encoding.UTF8.GetString(raw);

        Assert.False(text.EndsWith('\n'));
        Assert.DoesNotContain(", ", text);
        Assert.DoesNotContain(": ", text);
    }
}

/// Mirrors test_protocol.py's TestParseResponse.
public class ParseResponseTests
{
    [Fact]
    public void ParsesWellFormedResponse()
    {
        var raw = Encoding.UTF8.GetBytes("{\"Code\":200,\"Message\":\"OK\",\"Player\":{\"Handed\":\"RH\"}}");
        var resp = JsonFraming.ParseResponse(raw);

        Assert.Equal(200, resp.Code);
        Assert.Equal("OK", resp.Message);
        Assert.NotNull(resp.Player);
        Assert.Equal("RH", resp.Player!["Handed"].GetString());
    }

    [Fact]
    public void DefaultsMissingFields()
    {
        var resp = JsonFraming.ParseResponse(Encoding.UTF8.GetBytes("{}"));

        Assert.Equal(0, resp.Code);
        Assert.Equal("", resp.Message);
        Assert.Null(resp.Player);
    }

    [Fact]
    public void ThrowsFormatExceptionOnMalformedJson()
    {
        Assert.Throws<FormatException>(() => JsonFraming.ParseResponse(Encoding.UTF8.GetBytes("{not json")));
    }
}

/// Mirrors test_protocol.py's TestFindJsonEnd.
public class FindJsonEndTests
{
    [Fact]
    public void TwoCompleteObjectsInOneBuffer()
    {
        var buf = Encoding.UTF8.GetBytes("{\"a\":1}{\"b\":2}");
        var end = JsonFraming.FindJsonEnd(buf);

        Assert.Equal(7, end);
        Assert.Equal("{\"a\":1}", Encoding.UTF8.GetString(buf[..end!.Value]));

        var remainder = buf[end.Value..];
        var end2 = JsonFraming.FindJsonEnd(remainder);
        Assert.Equal("{\"b\":2}", Encoding.UTF8.GetString(remainder[..end2!.Value]));
    }

    [Fact]
    public void ObjectSplitAcrossTwoExtends()
    {
        var firstHalf = Encoding.UTF8.GetBytes("{\"a\":1,\"b\"");
        var secondHalf = Encoding.UTF8.GetBytes(":2}");
        Assert.Null(JsonFraming.FindJsonEnd(firstHalf));

        var buf = new byte[firstHalf.Length + secondHalf.Length];
        firstHalf.CopyTo(buf, 0);
        secondHalf.CopyTo(buf, firstHalf.Length);

        var end = JsonFraming.FindJsonEnd(buf);
        Assert.Equal(buf.Length, end);
    }

    [Fact]
    public void BraceInsideStringDoesNotAffectDepth()
    {
        var buf = Encoding.UTF8.GetBytes("{\"Message\":\"a { b } c\"}");
        var end = JsonFraming.FindJsonEnd(buf);
        Assert.Equal(buf.Length, end);
    }

    [Fact]
    public void EscapedQuoteInsideStringDoesNotEndStringEarly()
    {
        var buf = Encoding.UTF8.GetBytes("{\"Message\":\"say \\\"hi\\\" now\"}");
        var end = JsonFraming.FindJsonEnd(buf);
        Assert.Equal(buf.Length, end);
    }

    [Fact]
    public void NoCompleteFrameReturnsNull()
    {
        var buf = Encoding.UTF8.GetBytes(new string('{', 100) + "\"a\":1");
        Assert.Null(JsonFraming.FindJsonEnd(buf));
    }

    [Fact]
    public void Over64KbWithNoCompleteFrameReturnsNull()
    {
        var prefix = Encoding.UTF8.GetBytes("{\"a\":\"");
        var filler = new byte[65 * 1024];
        Array.Fill(filler, (byte)'x');
        var buf = new byte[prefix.Length + filler.Length];
        prefix.CopyTo(buf, 0);
        filler.CopyTo(buf, prefix.Length);

        Assert.True(buf.Length > 64 * 1024);
        Assert.Null(JsonFraming.FindJsonEnd(buf));
    }
}
