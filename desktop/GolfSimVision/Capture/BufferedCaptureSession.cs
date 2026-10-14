using System;
using System.Collections.Generic;

namespace GolfSimVision.Capture;

/// <summary>
/// ADR-0003's buffered-burst model: the capture session stays alive continuously: only
/// the ring buffer is gated by mode. FullShot mode keeps it off; Putting mode arms it
/// (a rolling pre-roll window); a stroke trigger switches it to Shipping, collecting the
/// in-progress window until EndStroke() delivers it and re-arms.
///
/// Pure logic -- no camera involved, fully unit-testable, mirroring capture.py's own
/// split (RingBuffer is a pure/testable class there; the camera loop itself isn't).
/// TriggerStroke()/EndStroke() are this ticket's manual-demo hooks -- a later ticket's
/// real detector calls them instead of a button.
/// </summary>
public sealed class BufferedCaptureSession
{
    private readonly RingBuffer<CapturedFrame> _ringBuffer;
    private readonly List<CapturedFrame> _shippingWindow = new();
    private readonly object _lock = new();

    private CaptureMode _mode = CaptureMode.FullShot;
    private RingBufferState _state = RingBufferState.Off;

    public BufferedCaptureSession(int ringBufferCapacity)
    {
        _ringBuffer = new RingBuffer<CapturedFrame>(ringBufferCapacity);
    }

    public CaptureMode Mode
    {
        get { lock (_lock) return _mode; }
    }

    public RingBufferState State
    {
        get { lock (_lock) return _state; }
    }

    public int RingBufferCount => _ringBuffer.Count;

    public Action<RingBufferState>? OnRingBufferStateChanged { get; set; }
    public Action<IReadOnlyList<CapturedFrame>>? OnWindowShipped { get; set; }

    public void SetMode(CaptureMode mode)
    {
        RingBufferState? newState = null;
        lock (_lock)
        {
            _mode = mode;
            if (mode == CaptureMode.FullShot)
            {
                _ringBuffer.Clear();
                newState = SetStateLocked(RingBufferState.Off);
            }
            else if (_state == RingBufferState.Off)
            {
                newState = SetStateLocked(RingBufferState.Armed);
            }
        }
        if (newState is { } s) OnRingBufferStateChanged?.Invoke(s);
    }

    /// Feed every captured frame here. Routes to the ring buffer while Armed, to the
    /// in-progress shipping window while Shipping. A no-op while Off (FullShot mode).
    public void Ingest(CapturedFrame frame)
    {
        lock (_lock)
        {
            switch (_state)
            {
                case RingBufferState.Armed:
                    _ringBuffer.Append(frame);
                    break;
                case RingBufferState.Shipping:
                    _shippingWindow.Add(frame);
                    break;
                case RingBufferState.Off:
                    break;
            }
        }
    }

    /// Snapshots the ring buffer's current pre-roll content as the window's lead-in,
    /// then starts collecting post-trigger frames into it. False if not Armed.
    public bool TriggerStroke()
    {
        RingBufferState? newState = null;
        bool triggered;
        lock (_lock)
        {
            triggered = _mode == CaptureMode.Putting && _state == RingBufferState.Armed;
            if (triggered)
            {
                _shippingWindow.Clear();
                _shippingWindow.AddRange(_ringBuffer.Snapshot());
                newState = SetStateLocked(RingBufferState.Shipping);
            }
        }
        if (newState is { } s) OnRingBufferStateChanged?.Invoke(s);
        return triggered;
    }

    /// Ends the shipping window, delivers it via OnWindowShipped, and re-arms.
    public bool EndStroke()
    {
        List<CapturedFrame>? window = null;
        RingBufferState? newState = null;
        lock (_lock)
        {
            if (_state == RingBufferState.Shipping)
            {
                window = new List<CapturedFrame>(_shippingWindow);
                _shippingWindow.Clear();
                _ringBuffer.Clear();
                newState = SetStateLocked(RingBufferState.Armed);
            }
        }
        if (newState is { } s) OnRingBufferStateChanged?.Invoke(s);
        if (window is null) return false;
        OnWindowShipped?.Invoke(window);
        return true;
    }

    /// Caller must hold _lock. Returns the new state if it changed, else null.
    private RingBufferState? SetStateLocked(RingBufferState newState)
    {
        if (_state == newState) return null;
        _state = newState;
        return newState;
    }
}
