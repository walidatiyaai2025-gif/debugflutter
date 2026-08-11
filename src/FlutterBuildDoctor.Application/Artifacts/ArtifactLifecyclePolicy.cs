using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Artifacts;

public sealed record ArtifactLifecycleInput(
    string Identity,
    string FileName,
    DateTimeOffset CreatedAt,
    int RetentionDays);

public sealed record ArtifactLifecycleItem(
    string Identity,
    string FileName,
    DateTimeOffset CreatedAtUtc,
    int RetentionDays,
    DateTimeOffset ExpiresAtUtc,
    bool Expired,
    string ReasonCode);

public sealed record ArtifactLifecycleResult(IReadOnlyList<ArtifactLifecycleItem> Artifacts, string Fingerprint);

public static partial class ArtifactLifecyclePolicy
{
    public const int MinRetentionDays = 1;
    public const int MaxRetentionDays = 90;
    public const int MaxArtifacts = 256;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".apk", ".aab", ".zip", ".json", ".log", ".txt"
    };

    public static ArtifactLifecycleResult Evaluate(IEnumerable<ArtifactLifecycleInput> artifacts, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        var materialized = artifacts.ToList();
        if (materialized.Count > MaxArtifacts)
        {
            throw new ArgumentOutOfRangeException(nameof(artifacts), "Artifact count exceeds the supported bound.");
        }

        var nowUtc = now.ToUniversalTime();
        var normalized = materialized.Select(item => Normalize(item, nowUtc))
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenBy(item => item.Identity, StringComparer.Ordinal)
            .ToArray();

        var canonical = string.Join('\n', normalized.Select(item =>
            $"{item.Identity}|{item.FileName}|{item.CreatedAtUtc:O}|{item.RetentionDays}|{item.ExpiresAtUtc:O}|{item.Expired}|{item.ReasonCode}"));
        return new ArtifactLifecycleResult(normalized, Hash(canonical));
    }

    public static ArtifactLifecycleItem Normalize(ArtifactLifecycleInput input, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(input);
        var identity = NormalizeIdentity(input.Identity);
        var fileName = NormalizeFileName(input.FileName);
        var retention = Math.Clamp(input.RetentionDays, MinRetentionDays, MaxRetentionDays);
        var createdAt = input.CreatedAt.ToUniversalTime();
        var expiresAt = createdAt.AddDays(retention);
        var expired = nowUtc.ToUniversalTime() >= expiresAt;
        return new ArtifactLifecycleItem(identity, fileName, createdAt, retention, expiresAt, expired, expired ? "expired" : "active");
    }

    public static string NormalizeIdentity(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (!IdentityRegex().IsMatch(normalized))
        {
            throw new ArgumentException("Artifact identity is invalid.", nameof(value));
        }
        return normalized;
    }

    public static string NormalizeFileName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var trimmed = value.Trim();
        if (trimmed.Contains(Path.DirectorySeparatorChar) || trimmed.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException("Artifact file name must not contain directory separators.", nameof(value));
        }

        var extension = Path.GetExtension(trimmed);
        if (!SupportedExtensions.Contains(extension))
        {
            throw new ArgumentException("Artifact extension is not supported.", nameof(value));
        }

        var baseName = Path.GetFileNameWithoutExtension(trimmed);
        var normalizedBase = FileInvalidRegex().Replace(baseName, "-").Trim('-', '.');
        if (normalizedBase.Length == 0)
        {
            throw new ArgumentException("Artifact file name is invalid.", nameof(value));
        }
        return normalizedBase + extension.ToLowerInvariant();
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentityRegex();

    [GeneratedRegex("[^A-Za-z0-9._-]+", RegexOptions.CultureInvariant)]
    private static partial Regex FileInvalidRegex();
}
