using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B1650;

public sealed class CacheFreshnessPolicyTests
{
    [Fact]
    public void Evaluate_ClassifiesFreshStaleAndFutureEntries()
    {
        var now = DateTimeOffset.Parse("2026-08-13T12:00:00Z");
        var fresh = CacheFreshnessPolicy.Evaluate("entry", now.AddMinutes(-5), now, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(1));
        Assert.True(fresh.Fresh);
        Assert.False(fresh.RefreshRequired);
        Assert.Equal("cache-entry-fresh", fresh.ReasonCode);

        var stale = CacheFreshnessPolicy.Evaluate("entry", now.AddMinutes(-20), now, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(1));
        Assert.False(stale.Fresh);
        Assert.True(stale.RefreshRequired);
        Assert.Equal("cache-entry-stale", stale.ReasonCode);

        var future = CacheFreshnessPolicy.Evaluate("entry", now.AddMinutes(5), now, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(1));
        Assert.True(future.FutureDated);
        Assert.Equal("cache-entry-future-dated", future.ReasonCode);
    }

    [Fact]
    public void Evaluate_RejectsNegativeFutureTolerance()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CacheFreshnessPolicy.Evaluate("entry", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(-1)));
    }
}
