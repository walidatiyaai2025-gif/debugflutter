using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record EnvironmentSnapshotEntry(string Name, string Value, bool Redacted);

public sealed record EnvironmentSnapshotDecision(
    string SnapshotIdentity,
    IReadOnlyList<EnvironmentSnapshotEntry> Variables,
    string CanonicalPayload,
    string ReasonCode,
    string Fingerprint);

public static class EnvironmentSnapshotIntegrityPolicy
{
    public const int DefaultMaxVariables = 256;
    private static readonly Regex IdentityPattern = new("^[a-z0-9][a-z0-9._-]{0,127}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly string[] SecretMarkers =
    {
        "password", "passwd", "token", "secret", "api_key", "apikey", "authorization", "credential", "private_key"
    };

    public static EnvironmentSnapshotDecision Evaluate(
        string snapshotIdentity,
        IEnumerable<KeyValuePair<string, string?>> variables,
        int maxVariables = DefaultMaxVariables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        var identity = NormalizeIdentity(snapshotIdentity);
        maxVariables = Math.Clamp(maxVariables, 1, 2048);

        var byName = new Dictionary<string, EnvironmentSnapshotEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in variables)
        {
            var name = NormalizeVariableName(pair.Key);
            if (byName.ContainsKey(name))
            {
                throw new ArgumentException($"Duplicate environment variable '{name}'.", nameof(variables));
            }

            var secret = IsSecretName(name);
            var value = secret ? "[REDACTED]" : NormalizeValue(name, pair.Value ?? string.Empty);
            byName.Add(name, new EnvironmentSnapshotEntry(name, value, secret));

            if (byName.Count > maxVariables)
            {
                throw new ArgumentOutOfRangeException(nameof(variables), $"Environment snapshot exceeds the {maxVariables} variable limit.");
            }
        }

        var ordered = byName.Values.OrderBy(item => item.Name, StringComparer.Ordinal).ToArray();
        var payload = string.Join("\n", ordered.Select(item => $"{item.Name}={item.Value}"));
        return new EnvironmentSnapshotDecision(identity, ordered, payload, "environment-snapshot-valid", Hash(identity + "\n" + payload));
    }

    private static string NormalizeIdentity(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Snapshot identity is required.", nameof(value));
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (!IdentityPattern.IsMatch(normalized))
        {
            throw new ArgumentException($"Unsafe snapshot identity '{value}'.", nameof(value));
        }

        return normalized;
    }

    private static string NormalizeVariableName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Environment variable name is required.", nameof(value));
        }

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length > 128 || normalized.Any(char.IsControl) || normalized.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException($"Unsafe environment variable name '{value}'.", nameof(value));
        }

        return normalized;
    }

    private static bool IsSecretName(string name)
    {
        var lowered = name.ToLowerInvariant();
        return SecretMarkers.Any(marker => lowered.Contains(marker, StringComparison.Ordinal));
    }

    private static string NormalizeValue(string name, string value)
    {
        var trimmed = value.Trim();
        if (name.Equals("PATH", StringComparison.OrdinalIgnoreCase) || name.EndsWith("_PATH", StringComparison.OrdinalIgnoreCase))
        {
            return string.Join(';', trimmed.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase));
        }

        return trimmed.Length <= 1024 ? trimmed : trimmed[..1024];
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
