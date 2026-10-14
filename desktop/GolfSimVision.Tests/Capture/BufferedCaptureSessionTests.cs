using System.Collections.Generic;
using System.Linq;
using GolfSimVision.Capture;
using Xunit;

namespace GolfSimVision.Tests.Capture;

public class BufferedCaptureSessionTests
{
    private static CapturedFrame Frame(double t) => new(t, 4, 4, new byte[4 * 4 * 3]);

    [Fact]
    public void StartsInFullShotModeWithRingBufferOff()
    {
        var session = new BufferedCaptureSession(ringBufferCapacity: 10);
        Assert.Equal(CaptureMode.FullShot, session.Mode);
        Assert.Equal(RingBufferState.Off, session.State);
    }

    [Fact]
    public void IngestIsANoOpWhileOff()
    {
        var session = new BufferedCaptureSession(ringBufferCapacity: 10);
        session.Ingest(Frame(0.0));
        Assert.Equal(0, session.RingBufferCount);
    }

    [Fact]
    public void SwitchingToPuttingModeArmsTheRingBuffer()
    {
        var session = new BufferedCaptureSession(ringBufferCapacity: 10);
        var states = new List<RingBufferState>();
        session.OnRingBufferStateChanged = states.Add;

        session.SetMode(CaptureMode.Putting);

        Assert.Equal(RingBufferState.Armed, session.State);
        Assert.Equal(new[] { RingBufferState.Armed }, states);
    }

    [Fact]
    public void IngestFillsTheRingBufferWhileArmed()
    {
        var session = new BufferedCaptureSession(ringBufferCapacity: 3);
        session.SetMode(CaptureMode.Putting);

        for (var i = 0; i < 5; i++) session.Ingest(Frame(i));

        // Drop-oldest once over capacity, same as the ring buffer itself.
        Assert.Equal(3, session.RingBufferCount);
    }

    [Fact]
    public void SwitchingBackToFullShotModeClearsTheRingBufferAndTurnsItOff()
    {
        var session = new BufferedCaptureSession(ringBufferCapacity: 10);
        session.SetMode(CaptureMode.Putting);
        session.Ingest(Frame(0.0));

        session.SetMode(CaptureMode.FullShot);

        Assert.Equal(RingBufferState.Off, session.State);
        Assert.Equal(0, session.RingBufferCount);
    }

    [Fact]
    public void TriggerStrokeFailsWhenNotArmed()
    {
        var session = new BufferedCaptureSession(ringBufferCapacity: 10); // FullShot/Off
        Assert.False(session.TriggerStroke());
    }

    [Fact]
    public void TriggerStrokeSnapshotsThePreRollAndSwitchesToShipping()
    {
        var session = new BufferedCaptureSession(ringBufferCapacity: 10);
        session.SetMode(CaptureMode.Putting);
        session.Ingest(Frame(0.0));
        session.Ingest(Frame(1.0));

        var triggered = session.TriggerStroke();

        Assert.True(triggered);
        Assert.Equal(RingBufferState.Shipping, session.State);
    }

    [Fact]
    public void FramesIngestedDuringShippingJoinTheWindowNotTheRingBuffer()
    {
        var session = new BufferedCaptureSession(ringBufferCapacity: 10);
        IReadOnlyList<CapturedFrame>? shipped = null;
        session.OnWindowShipped = w => shipped = w;

        session.SetMode(CaptureMode.Putting);
        session.Ingest(Frame(0.0)); // pre-roll
        session.TriggerStroke();
        session.Ingest(Frame(1.0)); // post-trigger
        session.Ingest(Frame(2.0)); // post-trigger
        session.EndStroke();

        Assert.NotNull(shipped);
        Assert.Equal(new[] { 0.0, 1.0, 2.0 }, shipped!.Select(f => f.TimestampSeconds));
    }

    [Fact]
    public void EndStrokeReArmsForTheNextPutt()
    {
        var session = new BufferedCaptureSession(ringBufferCapacity: 10);
        session.SetMode(CaptureMode.Putting);
        session.Ingest(Frame(0.0));
        session.TriggerStroke();
        session.Ingest(Frame(1.0));

        session.EndStroke();

        Assert.Equal(RingBufferState.Armed, session.State);
        Assert.Equal(0, session.RingBufferCount); // cleared, ready for the next pre-roll
    }

    [Fact]
    public void EndStrokeFailsWhenNotShipping()
    {
        var session = new BufferedCaptureSession(ringBufferCapacity: 10);
        session.SetMode(CaptureMode.Putting); // Armed, not Shipping
        Assert.False(session.EndStroke());
    }
}
