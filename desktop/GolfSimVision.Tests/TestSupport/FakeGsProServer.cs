using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using GolfSimVision.Simulator;

namespace GolfSimVision.Tests.TestSupport;

/// <summary>
/// A minimal TCP listener standing in for GSPro, for SimulatorConnector tests. Mirrors
/// prototypes/gspro-harness/test_protocol.py's FakeGSProServer.
/// </summary>
public sealed class FakeGsProServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly List<TcpClient> _connections = new();
    private readonly object _connectionsLock = new();
    private readonly ManualResetEventSlim _accepted = new(false);
    private readonly Thread _acceptThread;

    public FakeGsProServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "fake-gspro-accept" };
        _acceptThread.Start();
    }

    public int Port { get; }

    private void AcceptLoop()
    {
        while (true)
        {
            TcpClient conn;
            try
            {
                // Blocks until a client connects or _listener.Stop() (in Dispose) forces
                // this to throw -- no polling loop needed.
                conn = _listener.AcceptTcpClient();
            }
            catch (SocketException) { return; }
            catch (ObjectDisposedException) { return; }

            lock (_connectionsLock) { _connections.Add(conn); }
            _accepted.Set();
        }
    }

    public bool WaitForConnection(TimeSpan? timeout = null)
        => _accepted.Wait(timeout ?? TimeSpan.FromSeconds(2));

    /// Blocks for one complete JSON frame from the most recent connection.
    public byte[] RecvFrame(TimeSpan? timeout = null)
    {
        TcpClient conn;
        lock (_connectionsLock)
        {
            if (_connections.Count == 0)
                throw new InvalidOperationException("no client has connected yet");
            conn = _connections[^1];
        }

        var stream = conn.GetStream();
        stream.ReadTimeout = (int)(timeout ?? TimeSpan.FromSeconds(2)).TotalMilliseconds;
        var buffer = new List<byte>();
        var readBuf = new byte[4096];

        while (true)
        {
            var end = JsonFraming.FindJsonEnd(buffer.ToArray());
            if (end is not null) return buffer.ToArray()[..end.Value];

            int n;
            try
            {
                n = stream.Read(readBuf, 0, readBuf.Length);
            }
            catch (IOException ex) when (ex.InnerException is SocketException { SocketErrorCode: SocketError.TimedOut })
            {
                throw new TimeoutException("fake server: timed out waiting for a full frame");
            }
            if (n == 0) throw new IOException("fake server: peer closed before a full frame arrived");
            buffer.AddRange(readBuf[..n]);
        }
    }

    public void SendToClient(byte[] raw)
    {
        TcpClient conn;
        lock (_connectionsLock) { conn = _connections[^1]; }
        conn.GetStream().Write(raw, 0, raw.Length);
    }

    public void Dispose()
    {
        _listener.Stop();
        _acceptThread.Join(TimeSpan.FromSeconds(2));
        lock (_connectionsLock)
        {
            foreach (var conn in _connections)
            {
                try { conn.Close(); } catch (Exception) { /* best-effort cleanup */ }
            }
        }
        _accepted.Dispose();
    }
}
