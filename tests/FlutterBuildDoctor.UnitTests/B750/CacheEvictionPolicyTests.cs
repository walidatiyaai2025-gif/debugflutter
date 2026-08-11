using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B750;

public sealed class CacheEvictionPolicyTests
{
    [Fact]
    public void Evaluate_EvictsExpiredThenLruWhilePreservingPinnedAndActive()
    {
        var now = new DateTimeOffset(2026, 8, 11, 18, 0, 0, TimeSpan.Zero);
        var result = CacheEvictionPolicy.Evaluate(new[]
        {
            new CacheEntryCandidate("expired", 10, now.AddHours(-4), now.AddHours(-3), now.AddMinutes(-1)),
            new CacheEntryCandidate("old", 20, now.AddHours(-4), now.AddHours(-2)),
            new CacheEntryCandidate("new", 20, now.AddHours(-2), now.AddHours(-1)),
            new CacheEntryCandidate("pinned", 30, now.AddHours(-4), now.AddHours(-4), IsPinned: true),
            new CacheEntryCandidate("active", 30, now.AddHours(-4), now.AddHours(-4), IsActive: true)
        }, 80, now);

        Assert.Contains("expired", result.Evicted);
        Assert.Contains("old", result.Evicted);
        Assert.Contains("pinned", result.Retained);
        Assert.Contains("active", result.Retained);
        Assert.Equal("cache-eviction-planned", result.ReasonCode);
        Assert.True(result.RetainedBytes <= result.ByteBudget);
    }

    [Fact]
    public void Evaluate_ReportsProtectedOverflowInsteadOfEvictingProtectedEntries()
    {
        var now = DateTimeOffset.UtcNow;
        var result = CacheEvictionPolicy.Evaluate(new[]
        {
            new CacheEntryCandidate("pinned", 100, now, now, IsPinned: true)
        }, 10, now);

        Assert.Empty(result.Evicted);
        Assert.Contains("pinned", result.Retained);
        Assert.Equal("cache-budget-exceeded-by-protected-entries", result.ReasonCode);
    }

    [Fact]
    public void Evaluate_IsDeterministicAcrossInputOrder()
    {
        var now = new DateTimeOffset(2026, 8, 11, 0, 0, 0, TimeSpan.Zero);
        var input = new[]
        {
            new CacheEntryCandidate("b", 10, now, now),
            new CacheEntryCandidate("a", 10, now, now)
        };

        var first = CacheEvictionPolicy.Evaluate(input, 10, now);
        var second = CacheEvictionPolicy.Evaluate(input.AsEnumerable().Reverse(), 10, now);

        Assert.Equal(first.Retained, second.Retained);
        Assert.Equal(first.Evicted, second.Evicted);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void Evaluate_RejectsNegativeCacheSize()
        => Assert.Throws<ArgumentOutOfRangeException>(() => CacheEvictionPolicy.Evaluate(new[]
        {
            new CacheEntryCandidate("bad", -1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        }, 100, DateTimeOffset.UtcNow));
}
