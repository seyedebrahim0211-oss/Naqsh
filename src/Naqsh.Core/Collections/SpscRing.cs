namespace Naqsh.Core.Collections;

/// <summary>
/// Fixed-capacity single-producer/single-consumer queue. Lock-free and allocation-free after
/// construction; used to pass frames between the render thread and worker threads.
/// </summary>
public sealed class SpscRing<T>
{
    private readonly T[] _items;
    private long _head; // next read (consumer)
    private long _tail; // next write (producer)

    public SpscRing(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _items = new T[capacity];
    }

    public int Capacity => _items.Length;

    public int Count => (int)(Volatile.Read(ref _tail) - Volatile.Read(ref _head));

    /// <summary>Producer only. Returns false when full.</summary>
    public bool TryEnqueue(T item)
    {
        var tail = _tail;
        if (tail - Volatile.Read(ref _head) >= _items.Length)
        {
            return false;
        }

        _items[tail % _items.Length] = item;
        Volatile.Write(ref _tail, tail + 1);
        return true;
    }

    /// <summary>Consumer only. Returns false when empty.</summary>
    public bool TryDequeue(out T item)
    {
        var head = _head;
        if (head >= Volatile.Read(ref _tail))
        {
            item = default!;
            return false;
        }

        var idx = head % _items.Length;
        item = _items[idx];
        _items[idx] = default!;
        Volatile.Write(ref _head, head + 1);
        return true;
    }
}
