using System;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record CacheFreshnessDecision(
    string EntryIdentity,
    DateTimeOffset CachedAtUtc,
    TimeSpan Age,
    TimeSpan FreshnessLifetime,
    TimeSpan FutureTolerance,
    bool Fresh,
    bool FutureDated,
    bool RefreshRequired,
    string ReasonCode,
    string Fingerprint);

public static class CacheFreshnessPolicy
{
    public static readonly TimeSpan MinLifetime = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(30);
    public static readonly TimeSpan MaxFutureTolerance = TimeSpan.FromMinutes(10);

    public static CacheFreshnessDecision Evaluate(string entryIdentity, DateTimeOffset cachedAt, DateTimeOffset now, TimeSpan freshnessLifetime, TimeSpan futureTolerance)
    {
        var identity = B1550PolicyHelpers.Identity(entryIdentity, nameof(entryIdentity));
        var cached = B1550PolicyHelpers.Utc(cachedAt);
        now = B1550PolicyHelpers.Utc(now);
        if (futureTolerance < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(futureTolerance));
        var tolerance = futureTolerance > MaxFutureTolerance ? MaxFutureTolerance : futureTolerance;
        var lifetime = freshnessLifetime < MinLifetime ? MinLifetime : freshnessLifetime > MaxLifetime ? MaxLifetime : freshnessLifetime;
        var futureDated = cached > now + tolerance;
        var age = cached > now ? TimeSpan.Zero : now - cached;
        var fresh = !futureDated && age <= lifetime;
        var refresh = !fresh;
        var reason = futureDated ? "cache-entry-future-dated" : fresh ? "cache-entry-fresh" : "cache-entry-stale";
        var payload = $"{identity}|{cached:O}|{now:O}|{age.Ticks}|{lifetime.Ticks}|{tolerance.Ticks}|{fresh}|{futureDated}";
        return new CacheFreshnessDecision(identity, cached, age, lifetime, tolerance, fresh, futureDated, refresh, reason, B1550PolicyHelpers.Fingerprint(payload));
    }
}
