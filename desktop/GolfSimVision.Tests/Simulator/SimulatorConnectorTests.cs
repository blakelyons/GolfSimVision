using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using GolfSimVision.Simulator;
using GolfSimVision.Tests.TestSupport;
using Xunit;

namespace GolfSimVision.Tests.Simulator;

/// Mirrors prototypes/gspro-harness/test_protocol.py's TestGSProDirectConnection, plus
/// two new cases (ReadyForNextShot, shot pacing) for this ticket's own additions.
public class SimulatorConnectorTests : IDisposable
{
    private readonly FakeGsProServer _server = new();
    private readonly SimulatorConnector _client;

    public SimulatorConnectorTests()
    {
        _client = new SimulatorConnector(
            host: "127.0.0.1",
            port: _server.Port,
            deviceId: "GolfSimVision",
            backoffSeconds: new[] { 0.05, 0.05, 0.1 });
    }

    public void Dispose()
    {
        _client.Disconnect();
        _server.Dispose();
    }

    [Fact]
    public void ConnectTransitionsDisabledToConnected()
    {
        Assert.Equal(ConnectionState.Disabled, _client.State);
        _client.Connect();

        Assert.True(WaitUntil.True(() => _client.State == ConnectionState.Connected));
        Assert.True(_server.WaitForConnection(TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public void DisconnectStopsTheConnectionThread()
    {
        _client.Connect();
        WaitUntil.True(() => _client.State == ConnectionState.Connected);

        _client.Disconnect();

        Assert.Equal(ConnectionState.Stopped, _client.State);
    }

    [Fact]
    public void SendThrowsWhenNotConnected()
    {
        var payload = new ShotPayload { DeviceID = "d", Units = "Yards", ShotNumber = 1, APIversion = "1" };
        Assert.Throws<InvalidOperationException>(() => _client.Send(payload));
    }

    [Fact]
    public void SendHeartbeatDeliversExpectedFields()
    {
        _client.Connect();
        WaitUntil.True(() => _client.State == ConnectionState.Connected);

        _client.SendHeartbeat(ready: true, ballDetected: false);
        var frame = _server.RecvFrame();

        using var doc = JsonDocument.Parse(frame);
        var root = doc.RootElement;
        Assert.Equal("GolfSimVision", root.GetProperty("DeviceID").GetString());
        Assert.Equal("1", root.GetProperty("APIversion").GetString());
        Assert.Equal(JsonValueKind.Number, root.GetProperty("ShotNumber").ValueKind);
        Assert.True(root.GetProperty("ShotDataOptions").GetProperty("IsHeartBeat").GetBoolean());
        Assert.True(root.GetProperty("ShotDataOptions").GetProperty("LaunchMonitorIsReady").GetBoolean());
        Assert.False(root.GetProperty("ShotDataOptions").GetProperty("LaunchMonitorBallDetected").GetBoolean());
    }

    [Fact]
    public void ShotNumberSeededFromEpochSecondsAndIncrements()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.InRange(_client.LastShotNumber, now - 5, now + 5);

        var first = _client.NextShotNumber();
        var second = _client.NextShotNumber();
        Assert.Equal(first + 1, second);
    }

    [Fact]
    public void OverrideShotNumber()
    {
        _client.OverrideShotNumber(42);
        Assert.Equal(42, _client.NextShotNumber());
        Assert.Equal(43, _client.NextShotNumber());
    }

    [Fact]
    public void OnReceiveAndOnResponseFireForInboundFrames()
    {
        var received = new ManualResetEventSlim(false);
        var rawFrames = new List<byte[]>();
        var responses = new List<GsProResponse>();
        _client.OnReceive = frame => rawFrames.Add(frame);
        _client.OnResponse = r => { responses.Add(r); received.Set(); };

        _client.Connect();
        WaitUntil.True(() => _client.State == ConnectionState.Connected);
        _server.SendToClient(Encoding.UTF8.GetBytes("{\"Code\":200,\"Message\":\"OK\"}"));

        Assert.True(received.Wait(TimeSpan.FromSeconds(2)));
        Assert.Single(rawFrames);
        Assert.Equal("{\"Code\":200,\"Message\":\"OK\"}", Encoding.UTF8.GetString(rawFrames[0]));
        Assert.Single(responses);
        Assert.Equal(200, responses[0].Code);
        Assert.Equal("OK", responses[0].Message);
        Assert.Null(responses[0].Player);
    }

    [Fact]
    public void OnStatusFiresOnStateTransitions()
    {
        var states = new List<ConnectionState>();
        _client.OnStatus = states.Add;

        _client.Connect();
        WaitUntil.True(() => _client.State == ConnectionState.Connected);

        Assert.Contains(ConnectionState.Connecting, states);
        Assert.Contains(ConnectionState.Connected, states);
    }

    [Fact]
    public void DropForcesReconnectAndReconnectingState()
    {
        _client.Connect();
        WaitUntil.True(() => _client.State == ConnectionState.Connected);

        _client.Drop();

        Assert.True(WaitUntil.True(() => _client.State == ConnectionState.Reconnecting, TimeSpan.FromSeconds(2)));
        // The connection thread keeps running and reconnects against the same server.
        Assert.True(WaitUntil.True(() => _client.State == ConnectionState.Connected, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void ConnectingStateUsedBeforeFirstEverConnection()
    {
        // Points at a port nothing is listening on, so it never connects.
        using var deadClient = new SimulatorConnector(host: "127.0.0.1", port: 1, deviceId: "d", backoffSeconds: new[] { 0.05 });
        var states = new List<ConnectionState>();
        deadClient.OnStatus = states.Add;

        deadClient.Connect();
        WaitUntil.True(() => states.Contains(ConnectionState.Connecting) && states.Count >= 2);
        deadClient.Disconnect();

        Assert.DoesNotContain(ConnectionState.Reconnecting, states);
    }

    [Fact]
    public void ReadyForNextShotTracksInbound202AndResetsOnShotSend()
    {
        _client.Connect();
        WaitUntil.True(() => _client.State == ConnectionState.Connected);
        Assert.False(_client.IsReadyForNextShot);

        _server.SendToClient(Encoding.UTF8.GetBytes("{\"Code\":202,\"Message\":\"GSPro ready\"}"));
        Assert.True(WaitUntil.True(() => _client.IsReadyForNextShot));

        var codec = new GsProCodec("GolfSimVision", _client.NextShotNumber);
        _client.Send(codec.BuildPutt(speed: 5.0, hla: 0.0, vla: 0.0));

        Assert.False(_client.IsReadyForNextShot);
    }

    [Fact]
    public void ShotsArePacedWithAMinimumInterval()
    {
        _client.Connect();
        WaitUntil.True(() => _client.State == ConnectionState.Connected);
        var codec = new GsProCodec("GolfSimVision", _client.NextShotNumber);

        var start = DateTime.UtcNow;
        _client.Send(codec.BuildPutt(speed: 5.0, hla: 0.0, vla: 0.0));
        _server.RecvFrame();
        _client.Send(codec.BuildPutt(speed: 5.0, hla: 0.0, vla: 0.0));
        _server.RecvFrame();
        var elapsed = DateTime.UtcNow - start;

        Assert.True(elapsed >= SimulatorConnector.MinShotInterval);
    }
}
