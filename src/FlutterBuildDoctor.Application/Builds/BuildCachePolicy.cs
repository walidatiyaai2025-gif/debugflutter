using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Builds;

public enum BuildCacheScope
{
    Debug,
    Profile,
    Release
}

public sealed record BuildCacheSnapshot(
    string ToolchainFingerprint,
    string DependencyFingerprint,
    DateTimeOffset CreatedAt);

public sealed record BuildCacheDecision(
    string Namespace,
    BuildCacheScope Scope,
    IReadOnlyList<string> Segments,
    string CacheKey,
    bool ReuseExisting,
    string ReasonCode,
    DateTimeOffset CreatedAtUtc,
    string Fingerprint);

public static partial class BuildCachePolicy
{
    public const int MaxKeyLength = 128;
    public const int MaxSegments = 32;

    public static BuildCacheDecision Evaluate(
        string cacheNamespace,
        BuildCacheScope scope,
        IEnumerable<string> keySegments,
        string toolchainFingerprint,
        string dependencyFingerprint,
        DateTimeOffset createdAt,
        BuildCacheSnapshot? existing = null)
    {
        var normalizedNamespace = NormalizeNamespace(cacheNamespace);
        var segments = NormalizeSegments(keySegments);
        var toolchain = NormalizeFingerprint(toolchainFingerprint, nameof(toolchainFingerprint));
        var dependency = NormalizeFingerprint(dependencyFingerprint, nameof(dependencyFingerprint));
        var createdAtUtc = createdAt.ToUniversalTime();

        var segmentHash = Hash(string.Join('|', segments));
        var key = $"{normalizedNamespace}:{scope.ToString().ToLowerInvariant()}:{segmentHash[..24]}";
        if (key.Length > MaxKeyLength)
        {
            throw new InvalidOperationException("Cache key exceeded the supported bound.");
        }

        var reuse = false;
        var reason = "no-existing-cache";
        if (existing is not null)
        {
            var existingToolchain = NormalizeFingerprint(existing.ToolchainFingerprint, nameof(existing.ToolchainFingerprint));
            var existingDependency = NormalizeFingerprint(existing.DependencyFingerprint, nameof(existing.DependencyFingerprint));
            if (!existingToolchain.Equals(toolchain, StringComparison.OrdinalIgnoreCase))
            {
                reason = "toolchain-changed";
            }
            else if (!existingDependency.Equals(dependency, StringComparison.OrdinalIgnoreCase))
            {
                reason = "dependency-changed";
            }
            else
            {
                reuse = true;
                reason = "cache-valid";
            }
        }

        var canonical = string.Join('|', normalizedNamespace, scope, string.Join(',', segments), toolchain, dependency, createdAtUtc.ToString("O"), reuse, reason);
        return new BuildCacheDecision(normalizedNamespace, scope, segments, key, reuse, reason, createdAtUtc, Hash(canonical));
    }

    public static string NormalizeNamespace(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (!NamespaceRegex().IsMatch(normalized))
        {
            throw new ArgumentException("Cache namespace is invalid.", nameof(value));
        }
        return normalized;
    }

    public static IReadOnlyList<string> NormalizeSegments(IEnumerable<string> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var values = segments.ToList();
        if (values.Count > MaxSegments)
        {
            throw new ArgumentOutOfRangeException(nameof(segments), "Cache segment count exceeds the supported bound.");
        }

        var normalized = new List<string>(values.Count);
        foreach (var value in values)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            var segment = value.Trim().ToLowerInvariant();
            if (SecretRegex().IsMatch(segment))
            {
                throw new ArgumentException("Secret-bearing cache segments are not allowed.", nameof(segments));
            }
            normalized.Add(segment);
        }
        return normalized.AsReadOnly();
    }

    private static string NormalizeFingerprint(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (!FingerprintRegex().IsMatch(normalized))
        {
            throw new ArgumentException("Fingerprint must be 64 hexadecimal characters.", parameterName);
        }
        return normalized;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex NamespaceRegex();

    [GeneratedRegex("(password|passwd|token|secret|api[-_]?key|authorization)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretRegex();

    [GeneratedRegex("^[a-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex FingerprintRegex();
}
