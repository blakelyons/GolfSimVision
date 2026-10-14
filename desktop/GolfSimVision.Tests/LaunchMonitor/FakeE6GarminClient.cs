using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using GolfSimVision.Simulator;

namespace GolfSimVision.Tests.LaunchMonitor;

/// <summary>
/// Plays the Garmin Golf app's role against a real E6ConnectServer -- the mirror image
/// of SimulatorConnectorTests' FakeGsProServer (which played GSPro's role against our
/// client). Here we're testing our server, so the fake is the client.
/// </summary>
public sealed class FakeE6GarminClient : IDisposable
{
    private readonly TcpClient _client;

    public FakeE6GarminClient(int port)
    {
        _client = new TcpClient();
        _client.Connect("127.0.0.1", port);
    }

    public void SendJson(object message)
    {
        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json + "\n");
        _client.GetStream().Write(bytes, 0, bytes.Length);
    }

    /// Blocks for one complete JSON frame (newline-terminated by the server, but framed
    /// the same brace-balanced way as everything else in this codebase).
    public JsonDocument RecvFrame(TimeSpan? timeout = null)
    {
        var stream = _client.GetStream();
        stream.ReadTimeout = (int)(timeout ?? TimeSpan.FromSeconds(2)).TotalMilliseconds;
        var buffer = new List<byte>();
        var readBuf = new byte[4096];

        while (true)
        {
            var end = JsonFraming.FindJsonEnd(buffer.ToArray());
            if (end is not null) return JsonDocument.Parse(buffer.ToArray()[..end.Value]);

            int n;
            try
            {
                n = stream.Read(readBuf, 0, readBuf.Length);
            }
            catch (IOException ex) when (ex.InnerException is SocketException { SocketErrorCode: SocketError.TimedOut })
            {
                throw new TimeoutException("fake client: timed out waiting for a full frame");
            }
            if (n == 0) throw new IOException("fake client: server closed before a full frame arrived");
            buffer.AddRange(readBuf[..n]);
        }
    }

    public void Dispose() => _client.Close();
}
