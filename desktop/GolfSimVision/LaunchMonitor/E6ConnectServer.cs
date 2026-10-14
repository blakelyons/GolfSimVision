using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using GolfSimVision.Simulator;

namespace GolfSimVision.LaunchMonitor;

public enum E6ConnectionState
{
    Stopped,
    Listening,
    Connected,
}

/// <summary>
/// E6 Connect protocol server for the Garmin R10 via the Garmin Golf app (WiFi/TCP) --
/// ADR-0002: v1 receives R10 data this way rather than native Bluetooth. Ported from
/// V1's backend/monitors/r10.py, verified live in P0-4 against the current Garmin Golf
/// app. GolfSimVision owns this socket itself, same as it owns GSPro's (ADR-0001) --
/// nothing routes through gspro-r10.exe.
///
/// Unlike SimulatorConnector (a client connecting out), this is a server: it listens and
/// accepts whoever connects. "Reconnects cleanly if the phone sleeps or drops WiFi" falls
/// out of the accept loop's own structure, ported as-is: it never stops listening after a
/// client disconnects, so the next connection attempt (the app waking back up) is
/// accepted the same as the first.
///
/// Handshake sequence (unchanged from r10.py's own docstring, confirmed live in P0-4):
///   1. Client -> {"Type":"Handshake"}
///   2. Server -> {"Type":"Handshake","Challenge":"...","E6Version":...,"ProtocolVersion":...}
///   3. Client -> {"Type":"Challenge",...}
///   4. Server -> {"Type":"Authentication","Success":"true"}
///   5. Server sends {"Type":"SimCommand","SubType":"Ping"} every 10s
///
/// BallData field-name mapping from the R10's raw JSON to GolfSimVision.Simulator.
/// BallData is NOT independently verified against a real device payload -- r10.py itself
/// never parses these fields, only forwards the raw dict onward. This port assumes the
/// same field names GSPro's own BallData uses (a plausible, common E6 Connect
/// convention), which must be confirmed against a real R10 shot during hardware testing.
/// </summary>
public sealed class E6ConnectServer : IDisposable
{
    // The Garmin app echoes this back; r10.py never validates the response, and this
    // port doesn't either -- matching the reference exactly.
    public const string Challenge = "GolfSimVisionE6Challenge";

    private static readonly JsonSerializerOptions SerializeOptions = new() { WriteIndented = false };
    private static readonly JsonSerializerOptions InboundOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly int _port;
    private readonly TimeSpan _pingInterval;

    private readonly object _stateLock = new();
    private E6ConnectionState _state = E6ConnectionState.Stopped;

    private TcpListener? _listener;
    private readonly object _clientLock = new();
    private TcpClient? _client;

    private Thread? _acceptThread;

    public E6ConnectServer(int port = 2483, TimeSpan? pingInterval = null)
    {
        _port = port;
        _pingInterval = pingInterval ?? TimeSpan.FromSeconds(10);
    }

    public E6ConnectionState State
    {
        get { lock (_stateLock) return _state; }
    }

    public bool IsConnected => State == E6ConnectionState.Connected;

    /// The actual bound port once Start() has run -- lets callers pass port 0 (OS-assigned,
    /// used by the tests for isolation) and read back what was actually granted.
    public int Port => _listener is { } l ? ((IPEndPoint)l.LocalEndpoint).Port : _port;

    public Action<byte[]>? OnSend { get; set; }
    public Action<byte[]>? OnReceive { get; set; }
    public Action<BallData>? OnShotReceived { get; set; }
    public Action<E6ConnectionState>? OnStatus { get; set; }

    public void Start()
    {
        if (_listener is not null) return;
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();
        SetState(E6ConnectionState.Listening);

        _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "e6-accept" };
        _acceptThread.Start();
    }

    public void Stop()
    {
        _listener?.Stop();
        _listener = null;
        TcpClient? client;
        lock (_clientLock)
        {
            client = _client;
            _client = null;
        }
        try { client?.Close(); } catch (Exception) { }
        _acceptThread?.Join(TimeSpan.FromSeconds(2));
        _acceptThread = null;
        SetState(E6ConnectionState.Stopped);
    }

    private void AcceptLoop()
    {
        while (true)
        {
            TcpClient client;
            try
            {
                // Blocks until a client connects or Stop()'s _listener.Stop() forces
                // this to throw -- mirrors r10.py's own accept loop, which just keeps
                // looping "while self._running" rather than tearing down on one client's
                // disconnect.
                client = _listener!.AcceptTcpClient();
            }
            catch (SocketException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (NullReferenceException) { return; }

            // Defensively close any stale previous connection (shouldn't normally exist
            // -- each connection's own RecvLoop clears _client when it's done -- but a
            // misbehaving client can't always be trusted to close promptly). Same as
            // r10.py's accept_loop closing the old client before accepting the new one.
            TcpClient? stale;
            lock (_clientLock)
            {
                stale = _client;
                _client = client;
            }
            try { stale?.Close(); } catch (Exception) { }

            // Each connection owns its own CancellationTokenSource, never shared or
            // reassigned -- a stale connection's own cleanup (RecvLoop, below) must
            // never be able to cancel or close a newer connection's threads/socket.
            var cts = new CancellationTokenSource();
            SetState(E6ConnectionState.Connected);

            var recvThread = new Thread(() => RecvLoop(client, cts)) { IsBackground = true, Name = "e6-recv" };
            recvThread.Start();
            var pingThread = new Thread(() => PingLoop(client, cts)) { IsBackground = true, Name = "e6-ping" };
            pingThread.Start();
        }
    }

    private void RecvLoop(TcpClient client, CancellationTokenSource cts)
    {
        var buffer = new List<byte>();
        var readBuf = new byte[4096];
        NetworkStream stream;
        try
        {
            stream = client.GetStream();
        }
        catch (Exception)
        {
            EndConnection(client, cts);
            return;
        }

        while (!cts.IsCancellationRequested)
        {
            int n;
            try
            {
                stream.ReadTimeout = 200;
                n = stream.Read(readBuf, 0, readBuf.Length);
            }
            catch (IOException ex) when (ex.InnerException is SocketException { SocketErrorCode: SocketError.TimedOut })
            {
                continue;
            }
            catch (Exception)
            {
                break;
            }
            if (n == 0) break; // peer closed

            buffer.AddRange(readBuf[..n]);

            while (true)
            {
                var bufArray = buffer.ToArray();
                var end = JsonFraming.FindJsonEnd(bufArray);
                if (end is null) break;

                var frame = bufArray[..end.Value];
                buffer.RemoveRange(0, end.Value);
                OnReceive?.Invoke(frame);
                HandleMessage(client, frame, cts);
            }

            if (buffer.Count > JsonFraming.MaxFrameBytes) buffer.Clear();
        }

        EndConnection(client, cts);
    }

    private void PingLoop(TcpClient client, CancellationTokenSource cts)
    {
        var token = cts.Token;
        while (!token.IsCancellationRequested)
        {
            if (token.WaitHandle.WaitOne(_pingInterval)) return;
            if (token.IsCancellationRequested) return;
            SendTo(client, new SimCommand { SubType = "Ping" }, cts);
        }
    }

    /// <summary>
    /// Ends one connection: cancels its own ping thread, closes its own socket, and
    /// clears/transitions shared state only if this connection is still the current one
    /// -- a connection whose cleanup runs late (after a newer one has already replaced
    /// it in _client) must not disturb that newer connection.
    /// </summary>
    private void EndConnection(TcpClient client, CancellationTokenSource cts)
    {
        cts.Cancel();
        bool wasCurrent;
        lock (_clientLock)
        {
            wasCurrent = ReferenceEquals(_client, client);
            if (wasCurrent) _client = null;
        }
        try { client.Close(); } catch (Exception) { }
        // Deliberately not disposing cts here: PingLoop (a separate thread) may still be
        // blocked inside token.WaitHandle.WaitOne() at the instant Cancel() above
        // returns, and disposing a CancellationTokenSource while another thread is
        // using its WaitHandle is an explicitly documented race (ObjectDisposedException
        // on that thread). Each connection creates exactly one of these; left for GC
        // rather than racing a thread this class doesn't track a joinable handle for.
        if (wasCurrent) SetState(E6ConnectionState.Listening);
    }

    private void HandleMessage(TcpClient client, byte[] frame, CancellationTokenSource cts)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(frame);
        }
        catch (JsonException)
        {
            return;
        }

        using (doc)
        {
            var root = doc.RootElement;
            var type = GetType(root)?.ToLowerInvariant() ?? "";

            switch (type)
            {
                case "handshake":
                    SendTo(client, new HandshakeResponse { Challenge = Challenge }, cts);
                    break;
                case "challenge":
                    SendTo(client, new AuthenticationResponse(), cts);
                    break;
                case "setclubtype":
                    SendTo(client, new AckResponse { SubType = "SetClubType" }, cts);
                    break;
                case "setballdata":
                    SendTo(client, new AckResponse { SubType = "SetBallData" }, cts);
                    break;
                case "sendshot":
                    SendTo(client, new AckResponse { SubType = "SendShot" }, cts);
                    HandleShot(root);
                    break;
                case "heartbeat":
                case "ping":
                    SendTo(client, new SimCommand { SubType = "Ping" }, cts);
                    break;
            }
        }
    }

    private void HandleShot(JsonElement root)
    {
        if (!root.TryGetProperty("BallData", out var ballDataEl) || ballDataEl.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        BallData? ballData;
        try
        {
            ballData = JsonSerializer.Deserialize<BallData>(ballDataEl.GetRawText(), InboundOptions);
        }
        catch (JsonException)
        {
            return;
        }
        if (ballData is not null) OnShotReceived?.Invoke(ballData);
    }

    private static string? GetType(JsonElement root)
    {
        if (root.TryGetProperty("Type", out var t) && t.ValueKind == JsonValueKind.String) return t.GetString();
        if (root.TryGetProperty("type", out var tLower) && tLower.ValueKind == JsonValueKind.String) return tLower.GetString();
        return null;
    }

    private void SendTo<T>(TcpClient client, T message, CancellationTokenSource cts)
    {
        try
        {
            // Newline-terminated, matching r10.py's send() exactly -- the real Garmin
            // app this was verified against (P0-4) expects this framing.
            var json = JsonSerializer.SerializeToUtf8Bytes(message, SerializeOptions);
            var withNewline = new byte[json.Length + 1];
            json.CopyTo(withNewline, 0);
            withNewline[^1] = (byte)'\n';
            client.GetStream().Write(withNewline, 0, withNewline.Length);
            OnSend?.Invoke(withNewline);
        }
        catch (Exception)
        {
            EndConnection(client, cts);
        }
    }

    private void SetState(E6ConnectionState newState)
    {
        lock (_stateLock)
        {
            if (_state == newState) return;
            _state = newState;
        }
        OnStatus?.Invoke(newState);
    }

    public void Dispose() => Stop();
}
