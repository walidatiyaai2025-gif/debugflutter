using System.Security.Cryptography;
using System.Text;

namespace FlutterBuildDoctor.Application.Governance;

public enum ConfigurationSource
{
    Default = 0,
    Machine = 1,
    User = 2,
    Repository = 3
}

public sealed record ConfigurationEntry(ConfigurationSource Source, string Key, string Value);

public sealed record ConfigurationResolution(
    IReadOnlyDictionary<string, string> Values,
    string ReasonCode,
    string Fingerprint);

public static class ConfigurationPrecedencePolicy
{
    public const int MaxEntries = 256;
    public const int MaxValueLength = 2048;

    public static ConfigurationResolution Resolve(IEnumerable<ConfigurationEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var source = entries.ToArray();
        if (source.Length > MaxEntries)
            throw new ArgumentOutOfRangeException(nameof(entries));

        var normalized = source.Select(item => new ConfigurationEntry(
                item.Source,
                NormalizeKey(item.Key),
                NormalizeValue(item.Value)))
            .ToArray();

        var duplicate = normalized
            .GroupBy(item => $"{(int)item.Source}:{item.Key}", StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException("Duplicate configuration source/key pair.", nameof(entries));

        var values = normalized
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(item => item.Source).First())
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

        var payload = string.Join("\n", values.Select(item => $"{item.Key}={item.Value}"));
        return new ConfigurationResolution(values, "configuration-resolved", Hash(payload));
    }

    public static string NormalizeKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Configuration key is required.", nameof(value));

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > 128 || normalized.Any(char.IsControl) ||
            normalized.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-')))
            throw new ArgumentException("Configuration key is invalid.", nameof(value));
        return normalized;
    }

    public static string NormalizeValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var normalized = value.Trim();
        if (normalized.Length > MaxValueLength || normalized.Any(char.IsControl))
            throw new ArgumentException("Configuration value is invalid.", nameof(value));
        return normalized;
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
