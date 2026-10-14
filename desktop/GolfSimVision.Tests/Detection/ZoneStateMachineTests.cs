using GolfSimVision.Detection;
using GolfSimVision.Zones;
using Xunit;

namespace GolfSimVision.Tests.Detection;

/// <summary>
/// Mirrors zone_state_machine.py's behavior -- no existing Python test suite to compare
/// against (ported "unmodified" from V1 with none written there either), so these are
/// written directly from a careful reading of the source, exercising every branch
/// through the public Update()/State surface.
/// </summary>
public class ZoneStateMachineTests
{
    private sealed class TestClock
    {
        public double Seconds { get; set; }
        public double Now() => Seconds;
        public void Advance(double dt) => Seconds += dt;
    }

    // ppm=2.0 px/mm chosen for round numbers: a normalized-x delta of d maps to
    // d * 1280 / 2.0 = d * 640 mm.
    private const double Ppm = 2.0;

    private static BallDetection At(double x, double y = 0.5, double ppm = Ppm, double conf = 0.9)
        => new(x, y, 0.02, 0.02, conf, ppm);

    private static ZoneStateMachine CreateMachine(TestClock clock)
        => new(ZoneConfig.Default, frameWidth: 1280, frameHeight: 720, clock: clock.Now);

    [Fact]
    public void IdleTransitionsToWaitingWhenBallEntersStartZone()
    {
        var clock = new TestClock();
        var sm = CreateMachine(clock);

        var result = sm.Update(At(0.15)); // inside default start zone (x in [0.05, 0.30])

        Assert.Null(result);
        Assert.Equal(PuttState.Waiting, sm.State);
    }

    [Fact]
    public void IdleStaysIdleWhenBallOutsideStartZone()
    {
        var clock = new TestClock();
        var sm = CreateMachine(clock);

        sm.Update(At(0.9));

        Assert.Equal(PuttState.Idle, sm.State);
    }

    [Fact]
    public void WaitingReturnsToIdleWhenBallMovesLeftOfZone()
    {
        var clock = new TestClock();
        var sm = CreateMachine(clock);
        sm.Update(At(0.15));

        sm.Update(At(0.01)); // left of zone_left (0.05)

        Assert.Equal(PuttState.Idle, sm.State);
    }

    [Fact]
    public void WaitingTransitionsToTrackingWhenBallExitsZoneRight()
    {
        var clock = new TestClock();
        var sm = CreateMachine(clock);
        sm.Update(At(0.15));

        sm.Update(At(0.35)); // right of zone_right (0.30)

        Assert.Equal(PuttState.Tracking, sm.State);
    }

    [Fact]
    public void WaitingTransitionsToTrackingAfterBallVanishesOnceStillLongEnough()
    {
        var clock = new TestClock();
        var sm = CreateMachine(clock);
        sm.Update(At(0.15));

        // 20 still frames (same position, within the 0.06 jitter tolerance) to clear
        // STILL_THRESHOLD_FRAMES.
        for (var i = 0; i < 20; i++)
        {
            clock.Advance(1.0 / 60);
            sm.Update(At(0.15));
        }

        clock.Advance(1.0 / 60);
        var result = sm.Update(null); // ball vanished

        Assert.Null(result);
        Assert.Equal(PuttState.Tracking, sm.State);
    }

    [Fact]
    public void StablePpmIsMedianOfRecentWaitingDetections()
    {
        var clock = new TestClock();
        var sm = CreateMachine(clock);
        sm.Update(At(0.15)); // Idle -> Waiting entry; this call itself records no sample
        sm.Update(At(0.15, ppm: 1.0));
        sm.Update(At(0.15, ppm: 2.0));
        sm.Update(At(0.15, ppm: 3.0));

        Assert.Equal(2.0, sm.StablePpm, precision: 6);
    }

    [Fact]
    public void StablePpmIsZeroWithFewerThanThreeSamples()
    {
        var clock = new TestClock();
        var sm = CreateMachine(clock);
        sm.Update(At(0.15));
        sm.Update(At(0.15));

        Assert.Equal(0.0, sm.StablePpm);
    }

    [Fact]
    public void FullPuttFromWaitingThroughGatewayProducesATrackReadyResult()
    {
        var clock = new TestClock();
        var sm = CreateMachine(clock);
        sm.Update(At(0.15)); // -> Waiting

        PuttFinalizeResult? result = null;
        // Exit the zone to the right, then keep moving right past the gateway (0.55 +
        // 0.10 = 0.65), one frame at a time.
        foreach (var x in new[] { 0.32, 0.40, 0.48, 0.56, 0.64, 0.70 })
        {
            clock.Advance(1.0 / 60);
            result = sm.Update(At(x));
            if (result is not null) break;
        }

        Assert.IsType<PuttFinalizeResult.TrackReady>(result);
        Assert.Equal(PuttState.Idle, sm.State); // always returns to Idle, never Complete
    }

    [Fact]
    public void TrackingTimesOutAndFinalizesAfterThreeSeconds()
    {
        var clock = new TestClock();
        var sm = CreateMachine(clock);
        sm.Update(At(0.15));
        sm.Update(At(0.35)); // -> Tracking, 2 seed points

        // Feed enough points to clear MIN_TRACKING_FRAMES, all short of the gateway, so
        // the only way out is a timeout.
        for (var i = 0; i < 4; i++)
        {
            clock.Advance(0.1);
            sm.Update(At(0.36 + (i * 0.01)));
        }

        clock.Advance(3.1); // exceeds TRACKING_TIMEOUT_S
        var result = sm.Update(At(0.40));

        Assert.NotNull(result);
        Assert.Equal(PuttState.Idle, sm.State);
    }

    [Fact]
    public void RejectsATrackWithTooFewPoints()
    {
        var clock = new TestClock();
        var sm = CreateMachine(clock);
        sm.Update(At(0.15));
        sm.Update(At(0.35)); // -> Tracking with 2 seed points, below MIN_TRACKING_FRAMES (5)

        clock.Advance(3.1);
        var result = sm.Update(At(0.36));

        var rejected = Assert.IsType<PuttFinalizeResult.Rejected>(result);
        Assert.Contains("track point", rejected.Reason);
    }

    [Fact]
    public void RejectsATrackThatSpansTooLittleTime()
    {
        var clock = new TestClock();
        var sm = CreateMachine(clock);
        sm.Update(At(0.15));
        sm.Update(At(0.35)); // -> Tracking, 2 points at the same instant (no clock advance)

        // Enough points to clear MIN_TRACKING_FRAMES, but with no time elapsed at all.
        for (var i = 0; i < 4; i++) sm.Update(At(0.36 + (i * 0.01)));
        var result = sm.Update(At(0.70)); // crosses the gateway -> finalizes immediately

        var rejected = Assert.IsType<PuttFinalizeResult.Rejected>(result);
        Assert.Contains("spans only", rejected.Reason);
    }

    [Fact]
    public void RejectsATrackWithTooLittleNetDisplacement()
    {
        var clock = new TestClock();
        // A very high ppm makes any normalized-pixel displacement translate to very few
        // real-world mm, so the gateway-crossing track below ends up well under
        // MIN_TRACK_DISPLACEMENT_MM despite crossing the gateway in normalized-x terms.
        var sm = new ZoneStateMachine(ZoneConfig.Default, 1280, 720, clock.Now);
        sm.Update(At(0.15, ppm: 500.0));
        sm.Update(At(0.35, ppm: 500.0));

        PuttFinalizeResult? result = null;
        foreach (var x in new[] { 0.40, 0.48, 0.56, 0.64, 0.70 })
        {
            clock.Advance(0.1);
            result = sm.Update(At(x, ppm: 500.0));
            if (result is not null) break;
        }

        var rejected = Assert.IsType<PuttFinalizeResult.Rejected>(result);
        Assert.Contains("net displacement", rejected.Reason);
    }

    [Fact]
    public void VanishFilterIgnoresAPutterHoverNearTheSeedPosition()
    {
        var clock = new TestClock();
        var sm = CreateMachine(clock);
        sm.Update(At(0.30)); // seed position once vanished

        for (var i = 0; i < 20; i++)
        {
            clock.Advance(1.0 / 60);
            sm.Update(At(0.30));
        }
        clock.Advance(1.0 / 60);
        sm.Update(null); // -> Tracking, vanish filter active, seed_x = 0.30

        // A detection within VANISH_FILTER_MIN_DX (0.04) of the seed -- a putter
        // hovering/retracting near where the ball was -- must not finalize or progress
        // the track.
        clock.Advance(1.0 / 60);
        var filtered = sm.Update(At(0.32));
        Assert.Null(filtered);
        Assert.Equal(PuttState.Tracking, sm.State);

        // A real move well past the filter clears it and tracking proceeds normally.
        PuttFinalizeResult? result = null;
        foreach (var x in new[] { 0.40, 0.50, 0.60, 0.70, 0.75 })
        {
            clock.Advance(0.05);
            result = sm.Update(At(x));
            if (result is not null) break;
        }
        Assert.IsType<PuttFinalizeResult.TrackReady>(result);
    }

    [Fact]
    public void HeadingGuardRejectsASingleWildlyOffDirectionPoint()
    {
        var clock = new TestClock();
        var sm = CreateMachine(clock);
        sm.Update(At(0.15));
        sm.Update(At(0.35)); // -> Tracking, heading established left-to-right along y=0.5

        clock.Advance(0.05);
        sm.Update(At(0.45)); // continues the established rightward heading

        // A single point moving sharply backward-and-up, well past the minimum step
        // size for the heading check to engage, should be ignored rather than corrupt
        // the track.
        clock.Advance(0.05);
        var duringGuard = sm.Update(At(0.20, y: 0.1));
        Assert.Null(duringGuard);
        Assert.Equal(PuttState.Tracking, sm.State);

        // Tracking continues normally afterward.
        PuttFinalizeResult? result = null;
        foreach (var x in new[] { 0.55, 0.64, 0.70 })
        {
            clock.Advance(0.05);
            result = sm.Update(At(x));
            if (result is not null) break;
        }
        Assert.IsType<PuttFinalizeResult.TrackReady>(result);
    }

    [Fact]
    public void TwoPuttsBackToBackBothRegisterIndependently()
    {
        var clock = new TestClock();
        var sm = CreateMachine(clock);

        PuttFinalizeResult? FeedOnePutt()
        {
            sm.Update(At(0.15));
            sm.Update(At(0.35));
            PuttFinalizeResult? r = null;
            foreach (var x in new[] { 0.42, 0.50, 0.58, 0.64, 0.70 })
            {
                clock.Advance(0.05);
                r = sm.Update(At(x));
                if (r is not null) break;
            }
            return r;
        }

        var first = FeedOnePutt();
        var second = FeedOnePutt();

        Assert.IsType<PuttFinalizeResult.TrackReady>(first);
        Assert.IsType<PuttFinalizeResult.TrackReady>(second);
    }

    [Fact]
    public void StateChangedFiresOnEveryTransition()
    {
        var clock = new TestClock();
        var sm = CreateMachine(clock);
        var states = new System.Collections.Generic.List<PuttState>();
        sm.OnStateChanged = states.Add;

        sm.Update(At(0.15)); // -> Waiting
        sm.Update(At(0.35)); // -> Tracking

        Assert.Equal(new[] { PuttState.Waiting, PuttState.Tracking }, states);
    }

    [Fact]
    public void ResetReturnsToIdleAndClearsState()
    {
        var clock = new TestClock();
        var sm = CreateMachine(clock);
        sm.Update(At(0.15));
        sm.Update(At(0.35));

        sm.Reset();

        Assert.Equal(PuttState.Idle, sm.State);
        Assert.Equal(0.0, sm.StablePpm);
    }
}
