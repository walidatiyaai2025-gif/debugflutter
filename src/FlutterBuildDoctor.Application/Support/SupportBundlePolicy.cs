using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Support;

public enum SupportBundleCategory
{
    Environment = 0,
    Problems = 1,
    Logs = 2,
    General = 3
}

public sealed record SupportBundleItem(
    string Name,
    string Content,
    SupportBundleCategory Category = SupportBundleCategory.General,
    bool IsBinary = false);

public sealed record SanitizedSupportBundleItem(
    string Name,
    string Content,
    SupportBundleCategory Category);

public sealed record SupportBundleManifest(
    IReadOnlyList<SanitizedSupportBundleItem> Items,
    string Fingerprint);

public static partial class SupportBundlePolicy
{
    public const int MaxItems = 64;
    public const int DefaultMaxLineLength = 500;

    private static readonly HashSet<string> SecretKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "passwd",
        "token",
        "secret",
        "apikey",
        "api_key",
        "keypassword",
        "storepassword"
    };

    public static SupportBundleManifest Build(
        IEnumerable<SupportBundleItem> items,
        string? environmentSummary = null,
        string? problemSummary = null,
        int maxLineLength = DefaultMaxLineLength)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (maxLineLength is < 32 or > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLineLength));
        }

        var materialized = items.ToList();
        if (!string.IsNullOrWhiteSpace(environmentSummary))
        {
            materialized.Add(new SupportBundleItem("environment-summary.txt", environmentSummary, SupportBundleCategory.Environment));
        }

        if (!string.IsNullOrWhiteSpace(problemSummary))
        {
            materialized.Add(new SupportBundleItem("problem-summary.txt", problemSummary, SupportBundleCategory.Problems));
        }

        if (materialized.Count > MaxItems)
        {
            throw new ArgumentOutOfRangeException(nameof(items), $"Support bundle cannot exceed {MaxItems} items.");
        }

        var sanitized = materialized
            .Select(item => SanitizeItem(item, maxLineLength))
            .OrderBy(item => item.Category)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var canonical = string.Join("\n--item--\n", sanitized.Select(item => $"{(int)item.Category}|{item.Name}\n{item.Content}"));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return new SupportBundleManifest(sanitized, fingerprint);
    }

    public static string SanitizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Support item name is required.", nameof(name));
        }

        var normalized = string.Join('_', name.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length > 80 || normalized.Any(character =>
                !(char.IsLetterOrDigit(character) || character is '.' or '_' or '-')))
        {
            throw new ArgumentException("Support item name contains unsupported characters.", nameof(name));
        }

        return normalized;
    }

    public static string RedactText(string text, int maxLineLength = DefaultMaxLineLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (maxLineLength is < 32 or > 4096) throw new ArgumentOutOfRangeException(nameof(maxLineLength));
        if (text.Contains('\0')) throw new ArgumentException("Binary content is not allowed in support text.", nameof(text));

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        return string.Join('\n', lines.Select(line => BoundLine(RedactLine(line), maxLineLength)));
    }

    private static SanitizedSupportBundleItem SanitizeItem(SupportBundleItem item, int maxLineLength)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.IsBinary || item.Content.Contains('\0'))
        {
            throw new ArgumentException($"Binary support item '{item.Name}' is not allowed.", nameof(item));
        }

        return new SanitizedSupportBundleItem(
            SanitizeName(item.Name),
            RedactText(item.Content, maxLineLength),
            item.Category);
    }

    private static string RedactLine(string line)
    {
        var separatorIndex = line.IndexOfAny(new[] { '=', ':' });
        if (separatorIndex > 0)
        {
            var key = line[..separatorIndex].Trim();
            var compactKey = key.Replace("-", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal);
            if (SecretKeys.Contains(key) || SecretKeys.Contains(compactKey))
            {
                return $"{line[..(separatorIndex + 1)]}[REDACTED]";
            }
        }

        var redacted = BearerTokenRegex().Replace(line, "Bearer [REDACTED]");
        redacted = InlineSecretRegex().Replace(redacted, match => $"{match.Groups[1].Value}{match.Groups[2].Value}[REDACTED]");
        return redacted;
    }

    private static string BoundLine(string line, int maxLineLength)
    {
        if (line.Length <= maxLineLength) return line;
        return line[..(maxLineLength - 3)] + "...";
    }

    [GeneratedRegex("(?i)Bearer\\s+[A-Za-z0-9._~+\\-/]+=*")]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex("(?i)(token|password|passwd|secret|api[-_]?key)(\\s*[:=]\\s*)\\S+")]
    private static partial Regex InlineSecretRegex();
}
