using System;
using System.Collections.Generic;
using GolfSimVision.Zones;

namespace GolfSimVision.Detection;

/// <summary>Result of a finalized track -- the quality gate either rejects it or it's
/// ready for PuttCalculator.</summary>
public abstract record PuttFinalizeResult
{
    public sealed record Rejected(string Reason, int PointCount) : PuttFinalizeResult;

    public sealed record TrackReady(IReadOnlyList<TrackPoint> Track) : PuttFinalizeResult;
}

/// <summary>
/// Ported from prototypes/webcam-putting/detection/zone_state_machine.py's
/// ZoneStateMachine, unmodified in logic. Consumes the real ZoneConfig from ticket #4
/// (Zones.ZoneConfig/StartZone) rather than a separately-invented shape. Driven
/// synchronously, one Update() call per frame, by a single caller -- no internal
/// locking, matching the Python original.
///
/// This is a genuinely live, wall-clock-driven state machine (STILL_THRESHOLD_FRAMES,
/// TRACKING_TIMEOUT_S etc. are real-time concepts), so time comes from an injectable
/// clock (seconds, monotonically increasing) rather than DateTime.UtcNow directly --
/// production uses a real Stopwatch-backed clock; tests inject a fake one for
/// deterministic timing.
/// </summary>
public sealed class ZoneStateMachine
{
    // Clears a putter face: ~50px at 1280px wide. See VANISH_FILTER_MIN_DX below.
    private const double VanishFilterMinDx = 0.04;

    // Heading-continuity guard (TRACKING only): a real rolling ball can't instantaneously
    // change heading by a large angle in one ~60fps step, even though its net path can
    // legitimately curve over many points. Catches a single corrupted point (a
    // false-positive off the putter/hand/shadow) before it reaches the trajectory fit.
    private const double MaxHeadingDeviationDegrees = 35.0;
    private const double MinStepMmForHeadingCheck = 20.0;

    private const int StillThresholdFrames = 20;

    // Track-quality gate (precision over recall, ADR-0007): a track this thin can't be
    // trusted to become a shot.
    private const int MinTrackingFrames = 5;
    private const double MinTrackDisplacementMm = 15.0; // ~1/3 of a golf ball diameter
    private const double MinTrackDurationSeconds = 0.05; // a handful of frames at 60-120fps
    private const double TrackingTimeoutSeconds = 3.0;
    private const int PpmSampleSize = 30;

    private readonly Func<double> _clock;

    private readonly List<TrackPoint> _track = new();
    private int _stillFrames;
    private BallDetection? _lastDetection;
    private double _lastDetectionTimestamp;
    private double _trackingStarted;
    private readonly List<double> _ppmSamples = new();
    private bool _vanishFilterActive;
    private double _vanishFilterSeedX;

    public ZoneStateMachine(ZoneConfig zone, int frameWidth = 1280, int frameHeight = 720, Func<double>? clock = null)
    {
        Zone = zone;
        FrameWidth = frameWidth;
        FrameHeight = frameHeight;
        _clock = clock ?? DefaultClock();
    }

    private static Func<double> DefaultClock()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        return () => stopwatch.Elapsed.TotalSeconds;
    }

    public ZoneConfig Zone { get; set; }
    public int FrameWidth { get; }
    public int FrameHeight { get; }

    public PuttState State { get; private set; } = PuttState.Idle;

    /// Fires whenever State changes -- a later integration point can drive the capture
    /// session's ring buffer (TriggerStroke on Waiting->Tracking, EndStroke on finalize)
    /// from this instead of a manual button.
    public Action<PuttState>? OnStateChanged { get; set; }

    /// <summary>Median pixels-per-mm from recent WAITING detections (ball sitting still
    /// is the most reliable size measurement).</summary>
    public double StablePpm
    {
        get
        {
            if (_ppmSamples.Count < 3) return 0.0;
            var sorted = new List<double>(_ppmSamples);
            sorted.Sort();
            return sorted[sorted.Count / 2];
        }
    }

    public (BallDetection? Detection, double TimestampSeconds) LastKnownPosition => (_lastDetection, _lastDetectionTimestamp);

    /// <summary>Feed a new detection result (or null, meaning no ball found this frame).
    /// Returns a finalize result when a track completes (accepted or rejected), or null
    /// otherwise.</summary>
    public PuttFinalizeResult? Update(BallDetection? detection)
    {
        if (detection is null) return UpdateWithNoDetection();

        return State switch
        {
            PuttState.Idle => UpdateIdle(detection),
            PuttState.Waiting => UpdateWaiting(detection),
            PuttState.Tracking => UpdateTracking(detection),
            _ => null,
        };
    }

    private PuttFinalizeResult? UpdateWithNoDetection()
    {
        if (State == PuttState.Waiting)
        {
            if (_stillFrames >= StillThresholdFrames)
            {
                SetState(PuttState.Tracking);
                _track.Clear();
                // Seed with the last real position so the first mid-flight detection
                // immediately gives a valid two-point speed calc.
                if (_lastDetection is { } last && _lastDetectionTimestamp > 0)
                {
                    _track.Add(new TrackPoint(last.X, last.Y, last.PixelsPerMm, _lastDetectionTimestamp));
                }
                _trackingStarted = _clock();
                _vanishFilterActive = _lastDetection is not null;
                _vanishFilterSeedX = _lastDetection?.X ?? 0.0;
            }
            else
            {
                _stillFrames = Math.Max(0, _stillFrames - 1);
            }
        }
        else if (State == PuttState.Tracking)
        {
            if (_track.Count >= MinTrackingFrames) return Finalize();
            if (TrackingTimedOut()) return Finalize();
        }
        return null;
    }

    private PuttFinalizeResult? UpdateIdle(BallDetection detection)
    {
        if (InStartZone(detection))
        {
            SetState(PuttState.Waiting);
            _stillFrames = 0;
            _lastDetection = detection;
            _lastDetectionTimestamp = _clock();
        }
        return null;
    }

    private PuttFinalizeResult? UpdateWaiting(BallDetection detection)
    {
        if (InStartZone(detection))
        {
            if (_lastDetection is { } last)
            {
                var dx = Math.Abs(detection.X - last.X);
                var dy = Math.Abs(detection.Y - last.Y);
                if (dx > 0.06 || dy > 0.06)
                {
                    if (_stillFrames < StillThresholdFrames) _stillFrames = 0;
                }
                else
                {
                    _stillFrames++;
                    RecordPpmSample(detection.PixelsPerMm);
                }
            }
            else
            {
                _stillFrames++;
                RecordPpmSample(detection.PixelsPerMm);
            }
            _lastDetection = detection;
            _lastDetectionTimestamp = _clock();
            return null;
        }

        var zoneRight = Zone.StartZone.X + Zone.StartZone.W;
        var zoneLeft = Zone.StartZone.X;
        if (detection.X > zoneRight)
        {
            SetState(PuttState.Tracking);
            _trackingStarted = _clock();
            _track.Clear();
            _vanishFilterActive = false;
            if (_lastDetection is { } last && _lastDetectionTimestamp > 0)
            {
                _track.Add(new TrackPoint(last.X, last.Y, last.PixelsPerMm, _lastDetectionTimestamp));
            }
            _track.Add(new TrackPoint(detection.X, detection.Y, detection.PixelsPerMm, _clock()));
        }
        else if (detection.X < zoneLeft)
        {
            SetState(PuttState.Idle);
            _stillFrames = 0;
        }
        else
        {
            _stillFrames++;
            RecordPpmSample(detection.PixelsPerMm);
            _lastDetection = detection;
            _lastDetectionTimestamp = _clock();
        }
        return null;
    }

    private PuttFinalizeResult? UpdateTracking(BallDetection detection)
    {
        if (_vanishFilterActive)
        {
            if (detection.X < _vanishFilterSeedX + VanishFilterMinDx)
            {
                return TrackingTimedOut() ? Finalize() : null;
            }
            _vanishFilterActive = false;
        }

        if (HeadingDeviationTooLarge(detection))
        {
            return TrackingTimedOut() ? Finalize() : null;
        }

        _track.Add(new TrackPoint(detection.X, detection.Y, detection.PixelsPerMm, _clock()));
        if (detection.X > Zone.GatewayX + 0.10) return Finalize();
        if (TrackingTimedOut()) return Finalize();
        return null;
    }

    private void RecordPpmSample(double ppm)
    {
        if (ppm is < 0.05 or > 3.0) return;
        _ppmSamples.Add(ppm);
        if (_ppmSamples.Count > PpmSampleSize) _ppmSamples.RemoveAt(0);
    }

    private bool HeadingDeviationTooLarge(BallDetection detection)
    {
        if (_track.Count < 2) return false;
        var first = _track[0];
        var last = _track[^1];
        var ppm = last.PixelsPerMm != 0 ? last.PixelsPerMm : detection.PixelsPerMm;
        if (ppm <= 0) return false;

        var estDxMm = (last.X - first.X) * FrameWidth / ppm;
        var estDyMm = (last.Y - first.Y) * FrameHeight / ppm;
        if (Math.Sqrt((estDxMm * estDxMm) + (estDyMm * estDyMm)) < MinStepMmForHeadingCheck) return false;

        var stepDxMm = (detection.X - last.X) * FrameWidth / ppm;
        var stepDyMm = (detection.Y - last.Y) * FrameHeight / ppm;
        if (Math.Sqrt((stepDxMm * stepDxMm) + (stepDyMm * stepDyMm)) < MinStepMmForHeadingCheck) return false;

        var establishedHeading = Math.Atan2(estDyMm, estDxMm) * 180.0 / Math.PI;
        var stepHeading = Math.Atan2(stepDyMm, stepDxMm) * 180.0 / Math.PI;
        var deviation = Math.Abs((((stepHeading - establishedHeading) + 180) % 360) - 180);
        return deviation > MaxHeadingDeviationDegrees;
    }

    private bool InStartZone(BallDetection d)
    {
        var sz = Zone.StartZone;
        return d.X >= sz.X && d.X <= sz.X + sz.W && d.Y >= sz.Y && d.Y <= sz.Y + sz.H;
    }

    private bool TrackingTimedOut() => _clock() - _trackingStarted > TrackingTimeoutSeconds;

    private PuttFinalizeResult Finalize()
    {
        SetState(PuttState.Idle);
        var track = new List<TrackPoint>(_track);
        _track.Clear();
        _vanishFilterActive = false;

        var issue = TrackQualityIssue(track);
        if (issue is not null) return new PuttFinalizeResult.Rejected(issue, track.Count);
        return new PuttFinalizeResult.TrackReady(track);
    }

    private string? TrackQualityIssue(IReadOnlyList<TrackPoint> track)
    {
        if (track.Count < MinTrackingFrames)
        {
            return $"only {track.Count} track point(s), need at least {MinTrackingFrames}";
        }

        var durationSeconds = track[^1].TimestampSeconds - track[0].TimestampSeconds;
        if (durationSeconds < MinTrackDurationSeconds)
        {
            return $"track spans only {durationSeconds * 1000:F0}ms, need at least {MinTrackDurationSeconds * 1000:F0}ms";
        }

        var ppm = track[^1].PixelsPerMm != 0 ? track[^1].PixelsPerMm : track[0].PixelsPerMm;
        if (ppm > 0)
        {
            var dxMm = (track[^1].X - track[0].X) * FrameWidth / ppm;
            var dyMm = (track[^1].Y - track[0].Y) * FrameHeight / ppm;
            var displacementMm = Math.Sqrt((dxMm * dxMm) + (dyMm * dyMm));
            if (displacementMm < MinTrackDisplacementMm)
            {
                return $"only {displacementMm:F1}mm of net displacement, need at least {MinTrackDisplacementMm}mm";
            }
        }

        return null;
    }

    private void SetState(PuttState newState)
    {
        if (State == newState) return;
        State = newState;
        OnStateChanged?.Invoke(newState);
    }

    public void Reset()
    {
        State = PuttState.Idle;
        _track.Clear();
        _stillFrames = 0;
        _lastDetection = null;
        _lastDetectionTimestamp = 0.0;
        _ppmSamples.Clear();
        _vanishFilterActive = false;
        _vanishFilterSeedX = 0.0;
    }
}
