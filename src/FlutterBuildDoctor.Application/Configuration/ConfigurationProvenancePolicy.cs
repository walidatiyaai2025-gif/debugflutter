using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Configuration;

public enum ConfigurationSource
{
    Discovery = 0,
    Environment = 1,
    Project = 2,
    ExplicitUser = 3
}

public sealed record ConfigurationEvidence(string Key, string Value, ConfigurationSource Source, DateTimeOffset ObservedAt, string Evidence);
public sealed record ConfigurationResolution(string Key, string? Value, ConfigurationSource? Source, bool Conflict, IReadOnlyList<ConfigurationEvidence> Evidence, string ReasonCode);
public sealed record ConfigurationProvenanceDecision(IReadOnlyList<ConfigurationResolution> Resolutions, string Fingerprint);

public static partial class ConfigurationProvenancePolicy
{
    public const int MaxRecordsPerKey = 20;
    public const int MaxValueLength = 2048;

    public static ConfigurationProvenanceDecision Resolve(IEnumerable<ConfigurationEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var normalized = evidence.Select(Normalize).ToArray();
        var resolutions = normalized
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => ResolveKey(group.Key.ToLowerInvariant(), group.ToArray()))
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToArray();

        var canonical = string.Join('\n', resolutions.Select(item =>
            $"{item.Key}|{item.Value}|{item.Source}|{item.Conflict}|{item.ReasonCode}|" +
            string.Join(';', item.Evidence.Select(e => $"{e.Source}:{e.Value}:{e.ObservedAt:O}:{e.Evidence}"))));
        return new ConfigurationProvenanceDecision(resolutions, Hash(canonical));
    }

    public static ConfigurationEvidence Normalize(ConfigurationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var key = NormalizeKey(evidence.Key);
        var value = NormalizeValue(evidence.Value);
        var sourceEvidence = NormalizeEvidence(evidence.Evidence);
        return evidence with { Key = key, Value = value, ObservedAt = evidence.ObservedAt.ToUniversalTime(), Evidence = sourceEvidence };
    }

    public static string NormalizeKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (!KeyRegex().IsMatch(normalized)) throw new ArgumentException("Configuration key is invalid.", nameof(value));
        return normalized;
    }

    public static string NormalizeValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Any(char.IsControl)) throw new ArgumentException("Configuration value contains control characters.", nameof(value));
        var normalized = value.Trim();
        if (normalized.Length > MaxValueLength) throw new ArgumentOutOfRangeException(nameof(value), "Configuration value exceeds the supported bound.");
        return normalized;
    }

    private static ConfigurationResolution ResolveKey(string key, ConfigurationEvidence[] evidence)
    {
        if (evidence.Length > MaxRecordsPerKey) throw new ArgumentOutOfRangeException(nameof(evidence), $"Configuration key '{key}' has too many provenance records.");
        var ordered = evidence
            .OrderByDescending(item => item.Source)
            .ThenByDescending(item => item.ObservedAt)
            .ThenBy(item => item.Value, StringComparer.Ordinal)
            .ToArray();
        ConfigurationSource? highest = ordered.Length == 0 ? null : ordered[0].Source;
        var top = highest is null ? Array.Empty<ConfigurationEvidence>() : ordered.Where(item => item.Source == highest.Value).ToArray();
        var values = top.Select(item => item.Value).Distinct(StringComparer.Ordinal).ToArray();
        var conflict = values.Length > 1;
        var selected = conflict || top.Length == 0 ? null : top[0];
        var reason = conflict ? "high-priority-conflict"
            : selected?.Source == ConfigurationSource.ExplicitUser ? "explicit-user-selected"
            : selected is null ? "no-value" : "highest-priority-selected";
        return new ConfigurationResolution(key, selected?.Value, selected?.Source, conflict, ordered, reason);
    }

    private static string NormalizeEvidence(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Any(char.IsControl)) throw new ArgumentException("Configuration evidence contains control characters.", nameof(value));
        var normalized = value.Trim();
        return normalized.Length <= 512 ? normalized : normalized[..512];
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [GeneratedRegex("^[a-z][a-z0-9_.-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyRegex();
}
