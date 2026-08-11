using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record CacheEntryCandidate(
    string Identity,
    long SizeBytes,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastAccessedAt,
    DateTimeOffset? ExpiresAt = null,
    bool IsPinned = false,
    bool IsActive = false);

public sealed record CacheEvictionDecision(
    IReadOnlyList<string> Retained,
    IReadOnlyList<string> Evicted,
    long RetainedBytes,
    long ByteBudget,
    string ReasonCode,
    string Fingerprint);

public static partial class CacheEvictionPolicy
{
    public const int MaxEntries = 5_000;
    public const long MaxByteBudget = 100L * 1024 * 1024 * 1024;

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentityPattern();

    public static CacheEvictionDecision Evaluate(
        IEnumerable<CacheEntryCandidate> entries,
        long requestedByteBudget,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var input = entries.ToArray();
        if (input.Length > MaxEntries)
        {
            throw new ArgumentOutOfRangeException(nameof(entries), "Cache entry count exceeds the supported bound.");
        }

        var budget = Math.Clamp(requestedByteBudget, 0, MaxByteBudget);
        var nowUtc = now.ToUniversalTime();
        var normalized = input.Select(Normalize).ToArray();
        var duplicate = normalized.GroupBy(entry => entry.Identity, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException("Cache contains duplicate identities.", nameof(entries));
        }

        var retained = normalized.ToDictionary(entry => entry.Identity, StringComparer.OrdinalIgnoreCase);
        var evicted = new List<string>();

        var expired = normalized
            .Where(entry => !entry.IsPinned && !entry.IsActive && entry.ExpiresAtUtc is not null && entry.ExpiresAtUtc <= nowUtc)
            .OrderBy(entry => entry.ExpiresAtUtc)
            .ThenBy(entry => entry.Identity, StringComparer.Ordinal)
            .ToArray();
        foreach (var entry in expired)
        {
            if (retained.Remove(entry.Identity))
            {
                evicted.Add(entry.Identity);
            }
        }

        long retainedBytes = retained.Values.Sum(entry => entry.SizeBytes);
        var lruCandidates = retained.Values
            .Where(entry => !entry.IsPinned && !entry.IsActive)
            .OrderBy(entry => entry.LastAccessedAtUtc)
            .ThenBy(entry => entry.CreatedAtUtc)
            .ThenBy(entry => entry.Identity, StringComparer.Ordinal)
            .ToArray();
        foreach (var entry in lruCandidates)
        {
            if (retainedBytes <= budget)
            {
                break;
            }
            if (retained.Remove(entry.Identity))
            {
                retainedBytes -= entry.SizeBytes;
                evicted.Add(entry.Identity);
            }
        }

        var retainedIds = retained.Keys.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var evictedIds = evicted.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var protectedOverflow = retainedBytes > budget;
        var reason = protectedOverflow ? "cache-budget-exceeded-by-protected-entries" : evictedIds.Length > 0 ? "cache-eviction-planned" : "cache-within-budget";
        var canonical = $"{budget}|{retainedBytes}|{string.Join(',', retainedIds)}|{string.Join(',', evictedIds)}|{reason}";
        return new CacheEvictionDecision(retainedIds, evictedIds, retainedBytes, budget, reason, Hash(canonical));
    }

    private static NormalizedCacheEntry Normalize(CacheEntryCandidate entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var identity = entry.Identity.Trim().ToLowerInvariant();
        if (!IdentityPattern().IsMatch(identity))
        {
            throw new ArgumentException("Cache entry identity is invalid.", nameof(entry));
        }
        if (entry.SizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entry), "Cache size cannot be negative.");
        }
        var created = entry.CreatedAt.ToUniversalTime();
        var accessed = entry.LastAccessedAt.ToUniversalTime();
        if (accessed < created)
        {
            throw new ArgumentException("Last-access timestamp cannot precede creation.", nameof(entry));
        }
        return new NormalizedCacheEntry(identity, entry.SizeBytes, created, accessed, entry.ExpiresAt?.ToUniversalTime(), entry.IsPinned, entry.IsActive);
    }

    private sealed record NormalizedCacheEntry(
        string Identity,
        long SizeBytes,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset LastAccessedAtUtc,
        DateTimeOffset? ExpiresAtUtc,
        bool IsPinned,
        bool IsActive);

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
