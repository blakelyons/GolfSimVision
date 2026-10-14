using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace GolfSimVision.Simulator;

/// <summary>
/// Owns the single TCP connection to GSPro's OpenConnect listener (127.0.0.1:921) --
/// ADR-0001: GolfSimVision owns this socket directly, no relay through gspro-r10.exe.
///
/// Ported from prototypes/gspro-harness/protocol.py's GSProDirect (itself ported from
/// openflight/sim/transport.py's TcpSimClient): connection lifecycle, JSON framing, and
/// reconnect/backoff, preserving the same vocabulary decision (GSProDirect, not
/// TcpSimClient; ConnectionState.Reconnecting, not RECONNECT_BACKOFF). No automatic
/// background heartbeat thread -- heartbeats are sent explicitly by the caller.
///
/// Two things new in this ticket, not present in the Phase 0 prototype:
///   - IsReadyForNextShot, tracking GSPro's inbound 202 "GSPro ready" rather than the
///     LaunchMonitorIsReady field we send (GSPRO_OPENCONNECT.md: LaunchMonitorIsReady
///     never drove GSPro's own readiness indicator, confirmed three separate times).
///   - A minimum pacing guard between shots (GSPRO_OPENCONNECT.md exp15/19: near-zero
///     gaps between sends get unreliable ack correlation).
/// </summary>
public sealed class SimulatorConnector : IDisposable
{
    public static readonly double[] DefaultBackoffSeconds = { 1.0, 2.0, 4.0, 8.0, 16.0, 30.0 };

    /// Not a Phase-0 finding with a specific "right" value -- a conservative guard
    /// against the exp15/19 rapid-fire problem, chosen to be well under any pace a human
    /// putting session would ever hit.
    public static readonly TimeSpan MinShotInterval = TimeSpan.FromMilliseconds(250);

    private const int ConnectTimeoutMs = 5000;
    private const int RecvPollTimeoutMs = 200;

    private readonly string _host;
    private readonly int _port;
    private readonly double[] _backoffSeconds;

    private readonly object _stateLock = new();
    private ConnectionState _state = ConnectionState.Disabled;

    private readonly object _sockLock = new();
    private TcpClient? _client;

    private readonly object _shotNumberLock = new();
    private int _shotNumber;

    private readonly object _lastSendLock = new();
    private DateTime? _lastShotSentAt;

    private CancellationTokenSource? _stopCts;
    private Thread? _connectionThread;

    private volatile bool _isReadyForNextShot;

    public string DeviceId { get; }
    public string Units { get; }
    public DateTime? ConnectedAt { get; private set; }

    public Action<byte[]>? OnSend { get; set; }
    public Action<byte[]>? OnReceive { get; set; }
    public Action<GsProResponse>? OnResponse { get; set; }
    public Action<ConnectionState>? OnStatus { get; set; }

    public SimulatorConnector(
        string host,
        int port,
        string deviceId,
        string units = "Yards",
        double[]? backoffSeconds = null)
    {
        _host = host;
        _port = port;
        DeviceId = deviceId;
        Units = units;
        _backoffSeconds = backoffSeconds ?? DefaultBackoffSeconds;
        _shotNumber = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    // --- state --------------------------------------------------------------

    public ConnectionState State
    {
        get { lock (_stateLock) return _state; }
    }

    public bool IsConnected => State == ConnectionState.Connected;

    /// Tracks GSPro's inbound 202 "GSPro ready", not the LaunchMonitorIsReady field we
    /// send -- see the class docstring. Reset to false whenever a shot (not a heartbeat)
    /// is sent, since GSPro takes 5-15s after a shot's 200 ack before it sends 202 again.
    public bool IsReadyForNextShot => _isReadyForNextShot;

    private void SetState(ConnectionState newState)
    {
        lock (_stateLock)
        {
            if (_state == newState) return;
            _state = newState;
        }
        OnStatus?.Invoke(newState);
    }

    // --- shot numbering -------------------------------------------------------

    public int LastShotNumber
    {
        get { lock (_shotNumberLock) return _shotNumber; }
    }

    /// Returns the ShotNumber to use for the next send, then increments it.
    public int NextShotNumber()
    {
        lock (_shotNumberLock)
        {
            var n = _shotNumber;
            _shotNumber++;
            return n;
        }
    }

    /// Forces the next NextShotNumber() call to return exactly `value`.
    public void OverrideShotNumber(int value)
    {
        lock (_shotNumberLock) { _shotNumber = value; }
    }

    // --- connect / disconnect / drop -------------------------------------------

    /// Starts the connection thread (idempotent). Non-blocking.
    public void Connect()
    {
        if (_connectionThread is { IsAlive: true }) return;
        _stopCts = new CancellationTokenSource();
        var token = _stopCts.Token;
        _connectionThread = new Thread(() => ConnectionLoop(token))
        {
            IsBackground = true,
            Name = "gspro-conn",
        };
        _connectionThread.Start();
    }

    /// Orderly shutdown: stops the connection thread and closes the socket.
    public void Disconnect()
    {
        _stopCts?.Cancel();
        CloseSocket(graceful: true);
        _connectionThread?.Join(TimeSpan.FromSeconds(3));
        _connectionThread = null;
        ConnectedAt = null;
        SetState(ConnectionState.Stopped);
    }

    /// Simulates an ungraceful disconnect: forces a TCP RST via SO_LINGER(0) instead of
    /// a clean FIN, then lets the connection loop's normal reconnect/backoff path take
    /// over -- the thread keeps running. Mirrors protocol.py's drop().
    public void Drop()
    {
        CloseSocket(graceful: false);
    }

    // --- send ----------------------------------------------------------------

    public void Send(ShotPayload payload)
    {
        var isShot = !payload.ShotDataOptions.IsHeartBeat && payload.ShotDataOptions.ContainsBallData;
        if (isShot) EnforceShotPacing();

        SendRaw(JsonFraming.SerializePayload(payload));

        if (isShot)
        {
            _isReadyForNextShot = false;
            lock (_lastSendLock) { _lastShotSentAt = DateTime.UtcNow; }
        }
    }

    /// Sends bytes exactly as given, bypassing SerializePayload -- for manual/raw testing
    /// (malformed-input/casing experiments), mirroring protocol.py's send_raw().
    public void SendRaw(byte[] raw)
    {
        lock (_sockLock)
        {
            if (_client is null)
                throw new InvalidOperationException("SendRaw() called while not connected");
            _client.GetStream().Write(raw, 0, raw.Length);
        }
        OnSend?.Invoke(raw);
    }

    public void SendHeartbeat(bool ready, bool ballDetected = false)
    {
        var payload = new ShotPayload
        {
            DeviceID = DeviceId,
            Units = Units,
            ShotNumber = NextShotNumber(),
            APIversion = "1",
            ShotDataOptions = new ShotDataOptions
            {
                ContainsBallData = false,
                ContainsClubData = false,
                LaunchMonitorIsReady = ready,
                LaunchMonitorBallDetected = ballDetected,
                IsHeartBeat = true,
            },
        };
        Send(payload);
    }

    private void EnforceShotPacing()
    {
        TimeSpan? wait = null;
        lock (_lastSendLock)
        {
            if (_lastShotSentAt is { } last)
            {
                var elapsed = DateTime.UtcNow - last;
                if (elapsed < MinShotInterval) wait = MinShotInterval - elapsed;
            }
        }
        if (wait is { } w && w > TimeSpan.Zero) Thread.Sleep(w);
    }

    // --- internals -------------------------------------------------------------

    private void CloseSocket(bool graceful)
    {
        TcpClient? client;
        lock (_sockLock)
        {
            client = _client;
            _client = null;
        }
        if (client is null) return;

        try
        {
            if (!graceful)
            {
                // LingerState(true, 0) forces a RST on close() instead of the normal FIN
                // handshake, simulating a dropped link rather than an orderly disconnect.
                client.LingerState = new LingerOption(true, 0);
            }
            client.Close();
        }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
    }

    private bool TryConnect()
    {
        var client = new TcpClient();
        try
        {
            var connectTask = client.ConnectAsync(_host, _port);
            if (!connectTask.Wait(ConnectTimeoutMs))
            {
                client.Close();
                return false;
            }
        }
        catch (Exception ex) when (ex is SocketException or AggregateException)
        {
            client.Close();
            return false;
        }

        lock (_sockLock) { _client = client; }
        ConnectedAt = DateTime.UtcNow;
        return true;
    }

    private double BackoffForAttempt(int attempt)
    {
        var idx = Math.Min(attempt, _backoffSeconds.Length - 1);
        return _backoffSeconds[idx];
    }

    private void ConnectionLoop(CancellationToken token)
    {
        var attempt = 0;
        var everConnected = false;

        while (!token.IsCancellationRequested)
        {
            SetState(ConnectionState.Connecting);
            if (TryConnect())
            {
                attempt = 0;
                everConnected = true;
                SetState(ConnectionState.Connected);
                RecvLoop(token);
                CloseSocket(graceful: true);
                ConnectedAt = null;
                if (token.IsCancellationRequested) break;
                // Connection dropped (or RecvLoop returned) -- fall through to reconnect.
            }

            var wait = BackoffForAttempt(attempt);
            // Connecting means "never yet connected" (e.g. wrong port); Reconnecting
            // means "worked once, then dropped."
            SetState(everConnected ? ConnectionState.Reconnecting : ConnectionState.Connecting);
            attempt++;
            token.WaitHandle.WaitOne(TimeSpan.FromSeconds(wait));
        }
    }

    private void RecvLoop(CancellationToken token)
    {
        var buffer = new List<byte>();
        var readBuf = new byte[4096];

        while (!token.IsCancellationRequested)
        {
            TcpClient? client;
            lock (_sockLock) { client = _client; }
            if (client is null) return;

            NetworkStream stream;
            try
            {
                stream = client.GetStream();
                stream.ReadTimeout = RecvPollTimeoutMs;
            }
            catch (Exception)
            {
                return;
            }

            int n;
            try
            {
                n = stream.Read(readBuf, 0, readBuf.Length);
            }
            catch (IOException ex) when (ex.InnerException is SocketException { SocketErrorCode: SocketError.TimedOut })
            {
                continue;
            }
            catch (Exception)
            {
                return;
            }
            if (n == 0) return; // peer closed

            buffer.AddRange(readBuf[..n]);

            while (true)
            {
                var bufArray = buffer.ToArray();
                var end = JsonFraming.FindJsonEnd(bufArray);
                if (end is null) break;

                var frame = bufArray[..end.Value];
                buffer.RemoveRange(0, end.Value);

                OnReceive?.Invoke(frame);
                try
                {
                    var response = JsonFraming.ParseResponse(frame);
                    if (response.Code == 202) _isReadyForNextShot = true;
                    OnResponse?.Invoke(response);
                }
                catch (FormatException)
                {
                }
            }

            if (buffer.Count > JsonFraming.MaxFrameBytes)
            {
                buffer.Clear();
            }
        }
    }

    public void Dispose()
    {
        Disconnect();
        _stopCts?.Dispose();
    }
}
