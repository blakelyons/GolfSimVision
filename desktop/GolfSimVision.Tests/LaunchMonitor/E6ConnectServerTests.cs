using System;
using System.Collections.Generic;
using System.Text.Json;
using GolfSimVision.LaunchMonitor;
using GolfSimVision.Simulator;
using GolfSimVision.Tests.TestSupport;
using Xunit;

namespace GolfSimVision.Tests.LaunchMonitor;

/// Mirrors SimulatorConnectorTests' pattern (per ticket #8's own acceptance criterion):
/// an in-process fake plays the other side of the connection against the real server.
public class E6ConnectServerTests : IDisposable
{
    private readonly E6ConnectServer _server = new(port: 0, pingInterval: TimeSpan.FromMilliseconds(50));

    public void Dispose() => _server.Dispose();

    [Fact]
    public void StartTransitionsStoppedToListening()
    {
        Assert.Equal(E6ConnectionState.Stopped, _server.State);

        _server.Start();

        Assert.Equal(E6ConnectionState.Listening, _server.State);
    }

    [Fact]
    public void AClientConnectingTransitionsToConnected()
    {
        _server.Start();
        using var client = new FakeE6GarminClient(_server.Port);

        Assert.True(WaitUntil.True(() => _server.State == E6ConnectionState.Connected));
    }

    [Fact]
    public void CompletesTheFullHandshake()
    {
        _server.Start();
        using var client = new FakeE6GarminClient(_server.Port);
        WaitUntil.True(() => _server.State == E6ConnectionState.Connected);

        client.SendJson(new { Type = "Handshake" });
        using var handshakeResponse = client.RecvFrame();
        Assert.Equal("Handshake", handshakeResponse.RootElement.GetProperty("Type").GetString());
        Assert.Equal(E6ConnectServer.Challenge, handshakeResponse.RootElement.GetProperty("Challenge").GetString());

        client.SendJson(new { Type = "Challenge", Response = "whatever-the-app-sends" });
        using var authResponse = client.RecvFrame();
        Assert.Equal("Authentication", authResponse.RootElement.GetProperty("Type").GetString());
        Assert.Equal("true", authResponse.RootElement.GetProperty("Success").GetString());
    }

    [Fact]
    public void SendShotAcksAndFiresOnShotReceivedWithDeserializedBallData()
    {
        _server.Start();
        using var client = new FakeE6GarminClient(_server.Port);
        WaitUntil.True(() => _server.State == E6ConnectionState.Connected);

        var received = new List<BallData>();
        _server.OnShotReceived = received.Add;

        client.SendJson(new { Type = "SendShot", BallData = new { Speed = 150.0, HLA = 1.0, VLA = 12.0, TotalSpin = 2500.0, SpinAxis = -5.0 } });

        using var ack = client.RecvFrame();
        Assert.Equal("ACK", ack.RootElement.GetProperty("Type").GetString());
        Assert.Equal("SendShot", ack.RootElement.GetProperty("SubType").GetString());

        Assert.True(WaitUntil.True(() => received.Count == 1));
        Assert.Equal(150.0, received[0].Speed);
        Assert.Equal(1.0, received[0].HLA);
        Assert.Equal(12.0, received[0].VLA);
        Assert.Equal(2500.0, received[0].TotalSpin);
        Assert.Equal(-5.0, received[0].SpinAxis);
    }

    [Fact]
    public void RespondsToHeartbeatWithSimCommandPing()
    {
        _server.Start();
        using var client = new FakeE6GarminClient(_server.Port);
        WaitUntil.True(() => _server.State == E6ConnectionState.Connected);

        client.SendJson(new { Type = "Heartbeat" });

        using var response = client.RecvFrame();
        Assert.Equal("SimCommand", response.RootElement.GetProperty("Type").GetString());
        Assert.Equal("Ping", response.RootElement.GetProperty("SubType").GetString());
    }

    [Fact]
    public void SendsUnsolicitedPingsOnTheConfiguredInterval()
    {
        _server.Start(); // constructed with a 50ms ping interval
        using var client = new FakeE6GarminClient(_server.Port);
        WaitUntil.True(() => _server.State == E6ConnectionState.Connected);

        using var ping = client.RecvFrame(TimeSpan.FromSeconds(1));
        Assert.Equal("SimCommand", ping.RootElement.GetProperty("Type").GetString());
        Assert.Equal("Ping", ping.RootElement.GetProperty("SubType").GetString());
    }

    [Fact]
    public void ReturnsToListeningAfterTheClientDisconnectsAndAcceptsANewOne()
    {
        _server.Start();
        var first = new FakeE6GarminClient(_server.Port);
        WaitUntil.True(() => _server.State == E6ConnectionState.Connected);

        first.Dispose(); // simulates the phone sleeping / dropping WiFi

        Assert.True(WaitUntil.True(() => _server.State == E6ConnectionState.Listening, TimeSpan.FromSeconds(2)));

        // The server never stopped listening -- a fresh connection (the phone waking
        // back up) is accepted the same as the first.
        using var second = new FakeE6GarminClient(_server.Port);
        Assert.True(WaitUntil.True(() => _server.State == E6ConnectionState.Connected));
    }

    [Fact]
    public void StopTransitionsToStopped()
    {
        _server.Start();
        _server.Stop();

        Assert.Equal(E6ConnectionState.Stopped, _server.State);
    }

    [Fact]
    public void OnStatusFiresOnEveryTransition()
    {
        var states = new List<E6ConnectionState>();
        _server.OnStatus = states.Add;

        _server.Start();
        using var client = new FakeE6GarminClient(_server.Port);
        WaitUntil.True(() => _server.State == E6ConnectionState.Connected);

        Assert.Equal(new[] { E6ConnectionState.Listening, E6ConnectionState.Connected }, states);
    }
}
