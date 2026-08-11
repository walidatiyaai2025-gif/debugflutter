using System.Security.Cryptography;
using System.Text;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record SecretReference(string Provider, string Alias, string Reference);

public sealed record SecretReferenceResolution(
    IReadOnlyList<SecretReference> References,
    IReadOnlyList<string> SafeDisplays,
    string ReasonCode,
    string Fingerprint);

public static class SecretReferencePolicy
{
    public const int MaxReferences = 64;

    public static SecretReferenceResolution Resolve(IEnumerable<SecretReference> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        var source = references.ToArray();
        if (source.Length > MaxReferences)
            throw new ArgumentOutOfRangeException(nameof(references));

        var normalized = source.Select(item => new SecretReference(
                NormalizeToken(item.Provider, nameof(item.Provider)),
                NormalizeToken(item.Alias, nameof(item.Alias)),
                NormalizeReference(item.Reference)))
            .GroupBy(item => $"{item.Provider}:{item.Alias}", StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(item => item.Provider, StringComparer.Ordinal)
            .ThenBy(item => item.Alias, StringComparer.Ordinal)
            .ToArray();

        var displays = normalized.Select(item => $"${{{item.Provider}:{item.Alias}}}").ToArray();
        var payload = string.Join("\n", normalized.Select(item => $"{item.Provider}|{item.Alias}|{item.Reference}"));
        return new SecretReferenceResolution(normalized, displays, "secret-references-safe", Hash(payload));
    }

    public static string NormalizeReference(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Secret reference is required.", nameof(value));
        if (value.Any(char.IsControl))
            throw new ArgumentException("Control characters are not allowed.", nameof(value));

        var normalized = value.Trim();
        if (!normalized.StartsWith("secret://", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains('=') || normalized.Contains(' ') || normalized.Length > 256)
            throw new ArgumentException("Inline secret values are not allowed.", nameof(value));
        return normalized.ToLowerInvariant();
    }

    private static string NormalizeToken(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Secret reference token is required.", parameterName);
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > 64 || normalized.Any(char.IsControl) ||
            normalized.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')))
            throw new ArgumentException("Secret reference token is invalid.", parameterName);
        return normalized;
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
