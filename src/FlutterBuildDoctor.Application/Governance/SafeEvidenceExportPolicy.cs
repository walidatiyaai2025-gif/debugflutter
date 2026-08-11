using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record EvidenceExportRecord(string Key, string Value, bool Sensitive = false);

public sealed record EvidenceExportEntry(string Key, string Value, string Sha256);

public sealed record SafeEvidenceExportDecision(
    string Identity,
    string FileName,
    IReadOnlyList<EvidenceExportEntry> Records,
    string ReasonCode,
    string Fingerprint);

public static partial class SafeEvidenceExportPolicy
{
    public const int MaxRecords = 1000;
    public const int MaxValueLength = 4096;
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase) { ".json", ".txt" };

    public static SafeEvidenceExportDecision Prepare(string identity, string fileName, IEnumerable<EvidenceExportRecord> records)
    {
        var normalizedIdentity = NormalizeIdentity(identity);
        var normalizedFileName = NormalizeFileName(fileName);
        ArgumentNullException.ThrowIfNull(records);
        var input = records.ToArray();
        if (input.Length > MaxRecords) throw new ArgumentOutOfRangeException(nameof(records), "Evidence export record count exceeds the supported bound.");

        var normalized = input.Select(NormalizeRecord)
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ThenBy(item => item.Value, StringComparer.Ordinal)
            .Select(item => new EvidenceExportEntry(item.Key, item.Value, Hash($"{item.Key}|{item.Value}")))
            .ToArray();
        var canonical = $"{normalizedIdentity}|{normalizedFileName}\n" + string.Join('\n', normalized.Select(item => $"{item.Key}|{item.Value}|{item.Sha256}"));
        return new SafeEvidenceExportDecision(normalizedIdentity, normalizedFileName, normalized, "evidence-export-ready", Hash(canonical));
    }

    public static string NormalizeIdentity(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (!IdentityRegex().IsMatch(normalized)) throw new ArgumentException("Evidence export identity is invalid.", nameof(value));
        return normalized;
    }

    public static string NormalizeFileName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var trimmed = value.Trim();
        if (!string.Equals(Path.GetFileName(trimmed), trimmed, StringComparison.Ordinal) || trimmed.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Evidence export file name must not contain path traversal.", nameof(value));
        }
        var normalized = InvalidFileCharsRegex().Replace(trimmed.ToLowerInvariant(), "-").Trim('-', '.');
        if (normalized.Length == 0 || !AllowedExtensions.Contains(Path.GetExtension(normalized)))
        {
            throw new ArgumentException("Evidence export file extension is not approved.", nameof(value));
        }
        return normalized;
    }

    private static EvidenceExportRecord NormalizeRecord(EvidenceExportRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.Key);
        ArgumentNullException.ThrowIfNull(record.Value);
        var key = record.Key.Trim().ToLowerInvariant();
        if (!KeyRegex().IsMatch(key)) throw new ArgumentException("Evidence export key is invalid.", nameof(record));
        var sensitive = record.Sensitive || SensitiveKeyRegex().IsMatch(key);
        var value = sensitive ? "[REDACTED]" : NormalizeValue(record.Value);
        return new EvidenceExportRecord(key, value, sensitive);
    }

    private static string NormalizeValue(string value)
    {
        if (value.Any(char.IsControl)) throw new ArgumentException("Evidence export value contains control characters.", nameof(value));
        var normalized = value.Trim();
        return normalized.Length <= MaxValueLength ? normalized : normalized[..MaxValueLength];
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentityRegex();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyRegex();

    [GeneratedRegex("(?:password|passwd|token|secret|authorization|api[-_]?key)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveKeyRegex();

    [GeneratedRegex("[^a-z0-9._-]+", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidFileCharsRegex();
}
