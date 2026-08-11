using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record ArtifactManifestEntry(string RelativePath, string Sha256, long SizeBytes);

public sealed record ArtifactManifestDecision(
    string ManifestIdentity,
    IReadOnlyList<ArtifactManifestEntry> Entries,
    string CanonicalPayload,
    string ReasonCode,
    string Fingerprint);

public static class ArtifactManifestIntegrityPolicy
{
    public const int DefaultMaxArtifacts = 512;
    private static readonly Regex IdentityPattern = new("^[a-z0-9][a-z0-9._-]{0,127}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ShaPattern = new("^[0-9a-f]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static ArtifactManifestDecision Evaluate(
        string manifestIdentity,
        IEnumerable<ArtifactManifestEntry> entries,
        int maxArtifacts = DefaultMaxArtifacts)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var identity = NormalizeIdentity(manifestIdentity);
        maxArtifacts = Math.Clamp(maxArtifacts, 1, 4096);

        var normalized = entries.Select(NormalizeEntry).ToArray();
        if (normalized.Length > maxArtifacts)
        {
            throw new ArgumentOutOfRangeException(nameof(entries), $"Manifest exceeds the {maxArtifacts} artifact limit.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in normalized)
        {
            if (!seen.Add(entry.RelativePath))
            {
                throw new ArgumentException($"Duplicate artifact path '{entry.RelativePath}'.", nameof(entries));
            }
        }

        var ordered = normalized.OrderBy(entry => entry.RelativePath, StringComparer.Ordinal).ToArray();
        var payload = string.Join("\n", ordered.Select(entry => $"{entry.RelativePath}|{entry.Sha256}|{entry.SizeBytes}"));
        return new ArtifactManifestDecision(identity, ordered, payload, "artifact-manifest-valid", Hash(identity + "\n" + payload));
    }

    private static ArtifactManifestEntry NormalizeEntry(ArtifactManifestEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.SizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entry), "Artifact size cannot be negative.");
        }

        var path = NormalizeRelativePath(entry.RelativePath);
        var sha = (entry.Sha256 ?? string.Empty).Trim().ToLowerInvariant();
        if (!ShaPattern.IsMatch(sha))
        {
            throw new ArgumentException($"Artifact '{path}' has an invalid SHA-256 hash.", nameof(entry));
        }

        return new ArtifactManifestEntry(path, sha, entry.SizeBytes);
    }

    private static string NormalizeRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Artifact relative path is required.", nameof(value));
        }

        var normalized = value.Trim().Replace('\\', '/');
        if (Path.IsPathRooted(normalized) || normalized.StartsWith('/', StringComparison.Ordinal) || normalized.Contains(':'))
        {
            throw new ArgumentException("Artifact path must be relative.", nameof(value));
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".." || segment.Any(char.IsControl)))
        {
            throw new ArgumentException("Artifact path contains unsafe segments.", nameof(value));
        }

        return string.Join('/', segments);
    }

    private static string NormalizeIdentity(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Manifest identity is required.", nameof(value));
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (!IdentityPattern.IsMatch(normalized))
        {
            throw new ArgumentException($"Unsafe manifest identity '{value}'.", nameof(value));
        }

        return normalized;
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
