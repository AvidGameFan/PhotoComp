namespace PhotoComp.Infrastructure;

/// <summary>
/// A fixed-capacity least-recently-used cache.
/// When a new item is inserted and the cache is full, the least recently accessed
/// item is evicted. An optional <paramref name="onEvict"/> callback is invoked on
/// eviction — use it to dispose values (e.g., Avalonia Bitmaps).
/// </summary>
public sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Action<TValue>? _onEvict;
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map;
    private readonly LinkedList<(TKey Key, TValue Value)> _list;

    public LruCache(int capacity, Action<TValue>? onEvict = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
        _onEvict = onEvict;
        _map = new(capacity);
        _list = new();
    }

    public int Count => _list.Count;

    /// <summary>Retrieves a cached value, promoting it to most-recently-used.</summary>
    public bool TryGet(TKey key, out TValue value)
    {
        if (_map.TryGetValue(key, out var node))
        {
            _list.Remove(node);
            _list.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
        value = default!;
        return false;
    }

    /// <summary>
    /// Inserts or replaces a value. If the cache is at capacity, the least recently
    /// used entry is evicted first. Replacing an existing key also triggers eviction
    /// of the old value.
    /// </summary>
    public void Set(TKey key, TValue value)
    {
        // Replace existing key
        if (_map.TryGetValue(key, out var existing))
        {
            _list.Remove(existing);
            _map.Remove(key);
            _onEvict?.Invoke(existing.Value.Value);
        }

        // Evict LRU if at capacity
        if (_list.Count >= _capacity && _list.Last is { } lru)
        {
            _map.Remove(lru.Value.Key);
            _list.RemoveLast();
            _onEvict?.Invoke(lru.Value.Value);
        }

        var node = _list.AddFirst((key, value));
        _map[key] = node;
    }

    /// <summary>
    /// Removes all entries, invoking <see cref="_onEvict"/> for each.
    /// </summary>
    public void Clear()
    {
        if (_onEvict is not null)
            foreach (var node in _list)
                _onEvict(node.Value);

        _list.Clear();
        _map.Clear();
    }
}
