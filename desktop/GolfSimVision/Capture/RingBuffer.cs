using System.Collections.Generic;

namespace GolfSimVision.Capture;

/// <summary>
/// Fixed-capacity buffer; oldest entries drop once full. Lock-protected: a capture
/// thread appends while the UI/detector reads. Ported from capture.py's RingBuffer
/// (there, a lock-protected deque for the same reason -- mutation during another
/// thread's iteration would otherwise raise/corrupt).
/// </summary>
public sealed class RingBuffer<T>
{
    private readonly LinkedList<T> _items = new();
    private readonly object _lock = new();

    public RingBuffer(int capacity)
    {
        Capacity = capacity;
    }

    public int Capacity { get; }

    public void Append(T item)
    {
        lock (_lock)
        {
            _items.AddLast(item);
            while (_items.Count > Capacity) _items.RemoveFirst();
        }
    }

    /// <summary>Items in capture order, oldest first.</summary>
    public IReadOnlyList<T> Snapshot()
    {
        lock (_lock)
        {
            return new List<T>(_items);
        }
    }

    public T? Latest()
    {
        lock (_lock)
        {
            return _items.Count == 0 ? default : _items.Last!.Value;
        }
    }

    public void Clear()
    {
        lock (_lock) { _items.Clear(); }
    }

    public int Count
    {
        get { lock (_lock) return _items.Count; }
    }
}
