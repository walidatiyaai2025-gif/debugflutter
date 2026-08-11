using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record ArtifactNameDecision(
    string Identity,
    string BaseName,
    string Version,
    string Channel,
    string Extension,
    string FileName,
    string ReasonCode,
    string Fingerprint);

public static class ArtifactNamingPolicy
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.Ordinal)
    {
        ".zip", ".apk", ".aab", ".msix", ".exe", ".json"
    };

    private static readonly HashSet<string> AllowedChannels = new(StringComparer.Ordinal)
    {
        "local", "dev", "beta", "rc", "release"
    };

    public const int MaxFileNameLength = 120;

    public static ArtifactNameDecision Create(string identity, string baseName, string version, string channel, string extension)
    {
        var normalizedIdentity = NormalizeToken(identity, nameof(identity));
        var normalizedBase = NormalizeBaseName(baseName);
        var normalizedVersion = NormalizeVersion(version);
        var normalizedChannel = NormalizeToken(channel, nameof(channel));
        if (!AllowedChannels.Contains(normalizedChannel))
            throw new ArgumentException("Artifact channel is unsupported.", nameof(channel));

        var normalizedExtension = extension.Trim().ToLowerInvariant();
        if (!normalizedExtension.StartsWith('.'))
            normalizedExtension = $".{normalizedExtension}";
        if (!AllowedExtensions.Contains(normalizedExtension))
            throw new ArgumentException("Artifact extension is unsupported.", nameof(extension));

        var fileName = $"{normalizedBase}-{normalizedVersion}-{normalizedChannel}{normalizedExtension}";
        if (fileName.Length > MaxFileNameLength)
            throw new ArgumentException("Artifact file name is too long.", nameof(baseName));

        var payload = $"{normalizedIdentity}|{normalizedBase}|{normalizedVersion}|{normalizedChannel}|{normalizedExtension}|{fileName}";
        return new ArtifactNameDecision(normalizedIdentity, normalizedBase, normalizedVersion, normalizedChannel, normalizedExtension, fileName, "artifact-name-valid", Hash(payload));
    }

    public static string NormalizeBaseName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Artifact base name is required.", nameof(value));
        var trimmed = value.Trim();
        if (trimmed.Any(char.IsControl) || trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            trimmed.Contains('/') || trimmed.Contains('\\'))
            throw new ArgumentException("Artifact base name is unsafe.", nameof(value));
        var normalized = Regex.Replace(trimmed.ToLowerInvariant(), @"\s+", "-");
        normalized = Regex.Replace(normalized, @"-+", "-").Trim('-');
        if (normalized.Length == 0 || normalized.Length > 80)
            throw new ArgumentException("Artifact base name is invalid.", nameof(value));
        return normalized;
    }

    public static string NormalizeVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Artifact version is required.", nameof(value));
        var normalized = value.Trim().ToLowerInvariant();
        if (!Regex.IsMatch(normalized, @"^\d+\.\d+\.\d+(?:-[0-9a-z.-]+)?$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Artifact version must be semantic version text.", nameof(value));
        return normalized;
    }

    private static string NormalizeToken(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Artifact token is required.", parameterName);
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > 80 || normalized.Any(char.IsControl))
            throw new ArgumentException("Artifact token is invalid.", parameterName);
        return normalized;
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
