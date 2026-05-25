using PhotoComp.Infrastructure;

namespace PhotoComp.Tests;

public class LruCacheTests
{
    // ── Basic get/set ─────────────────────────────────────────────────

    [Fact]
    public void Set_ThenTryGet_ReturnsValue()
    {
        var cache = new LruCache<string, int>(3);
        cache.Set("a", 1);
        Assert.True(cache.TryGet("a", out var val));
        Assert.Equal(1, val);
    }

    [Fact]
    public void TryGet_MissingKey_ReturnsFalse()
    {
        var cache = new LruCache<string, int>(3);
        Assert.False(cache.TryGet("missing", out _));
    }

    [Fact]
    public void Count_ReflectsInsertions()
    {
        var cache = new LruCache<string, int>(5);
        cache.Set("a", 1);
        cache.Set("b", 2);
        Assert.Equal(2, cache.Count);
    }

    // ── Eviction ──────────────────────────────────────────────────────

    [Fact]
    public void WhenAtCapacity_LruItemEvicted()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1); // a is LRU candidate
        cache.Set("b", 2);
        cache.Set("c", 3); // should evict "a"

        Assert.False(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
    }

    [Fact]
    public void AccessingItem_PromotesIt_SoOtherItemEvictedFirst()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.TryGet("a", out _); // promote "a"  → "b" is now LRU
        cache.Set("c", 3);        // should evict "b", not "a"

        Assert.True(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
    }

    [Fact]
    public void EvictionCallback_CalledWithEvictedValue()
    {
        var evicted = new List<int>();
        var cache = new LruCache<string, int>(1, onEvict: v => evicted.Add(v));

        cache.Set("a", 10);
        cache.Set("b", 20); // evicts "a"

        Assert.Single(evicted);
        Assert.Equal(10, evicted[0]);
    }

    [Fact]
    public void ReplacingExistingKey_EvictsOldValue_AndStoresNew()
    {
        var evicted = new List<int>();
        var cache = new LruCache<string, int>(3, onEvict: v => evicted.Add(v));

        cache.Set("a", 1);
        cache.Set("a", 99); // replace — should evict old value 1

        Assert.Single(evicted);
        Assert.Equal(1, evicted[0]);
        Assert.True(cache.TryGet("a", out var val));
        Assert.Equal(99, val);
    }

    [Fact]
    public void CapacityOne_WorksCorrectly()
    {
        var evicted = new List<string>();
        var cache = new LruCache<int, string>(1, onEvict: v => evicted.Add(v));

        cache.Set(1, "first");
        cache.Set(2, "second"); // evicts "first"
        cache.Set(3, "third");  // evicts "second"

        Assert.False(cache.TryGet(1, out _));
        Assert.False(cache.TryGet(2, out _));
        Assert.True(cache.TryGet(3, out var val));
        Assert.Equal("third", val);
        Assert.Equal(["first", "second"], evicted);
    }

    [Fact]
    public void Constructor_ThrowsOnZeroCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LruCache<string, int>(0));
    }

    // ── Clear ─────────────────────────────────────────────────────────

    [Fact]
    public void Clear_EmptiesCache()
    {
        var cache = new LruCache<string, int>(5);
        cache.Set("a", 1);
        cache.Set("b", 2);

        cache.Clear();

        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Clear_MakesAllKeysMiss()
    {
        var cache = new LruCache<string, int>(5);
        cache.Set("a", 1);
        cache.Set("b", 2);

        cache.Clear();

        Assert.False(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
    }

    [Fact]
    public void Clear_InvokesEvictionCallback_ForAllItems()
    {
        var evicted = new List<int>();
        var cache = new LruCache<string, int>(5, onEvict: v => evicted.Add(v));
        cache.Set("a", 10);
        cache.Set("b", 20);
        cache.Set("c", 30);

        cache.Clear();

        Assert.Equal(3, evicted.Count);
        Assert.Contains(10, evicted);
        Assert.Contains(20, evicted);
        Assert.Contains(30, evicted);
    }

    [Fact]
    public void Clear_OnEmptyCache_DoesNotThrow()
    {
        var cache = new LruCache<string, int>(5);
        var ex = Record.Exception(() => cache.Clear());
        Assert.Null(ex);
    }

    [Fact]
    public void Clear_AllowsNewInsertsAfterwards()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.Clear();

        cache.Set("c", 3);

        Assert.True(cache.TryGet("c", out var val));
        Assert.Equal(3, val);
        Assert.Equal(1, cache.Count);
    }
}
