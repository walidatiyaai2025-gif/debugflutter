using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

public enum ClassifiedErrorCategory
{
    Unknown = 0,
    Build = 1,
    Toolchain = 2,
    Network = 3,
    Filesystem = 4,
    Configuration = 5
}

public enum ClassifiedErrorSeverity
{
    Warning = 0,
    Error = 1,
    Critical = 2
}

public sealed record ErrorClassificationRequest(string Code, string Message, IReadOnlyCollection<string>? Evidence = null);

public sealed record ErrorClassificationDecision(
    string Code,
    string MessageSummary,
    ClassifiedErrorCategory Category,
    ClassifiedErrorSeverity Severity,
    bool Retryable,
    bool RequiresUserAction,
    string GroupKey,
    IReadOnlyList<string> Evidence,
    string ReasonCode,
    string Fingerprint);

public static partial class ErrorClassificationPolicy
{
    public const int MaxEvidenceRecords = 32;
    public const int MaxEvidenceLength = 512;
    public const int MaxMessageLength = 512;

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    public static ErrorClassificationDecision Evaluate(ErrorClassificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var code = NormalizeCode(request.Code);
        var message = NormalizeSummary(request.Message, MaxMessageLength);
        var evidence = (request.Evidence ?? Array.Empty<string>())
            .Take(MaxEvidenceRecords)
            .Select(value => NormalizeSummary(value, MaxEvidenceLength))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var text = $"{code} {message}".ToLowerInvariant();
        var category = ClassifyCategory(text);
        var severity = ClassifySeverity(text, category);
        var retryable = category == ClassifiedErrorCategory.Network
            || text.Contains("timeout", StringComparison.Ordinal)
            || text.Contains("temporar", StringComparison.Ordinal)
            || text.Contains("busy", StringComparison.Ordinal);
        var userAction = category is ClassifiedErrorCategory.Configuration or ClassifiedErrorCategory.Filesystem
            || text.Contains("permission", StringComparison.Ordinal)
            || text.Contains("credential", StringComparison.Ordinal)
            || text.Contains("certificate", StringComparison.Ordinal);
        var groupKey = $"{category.ToString().ToLowerInvariant()}:{code}";
        var reason = "error-classified";
        var canonical = $"{groupKey}|{severity}|{retryable}|{userAction}|{message}|{string.Join('|', evidence)}";
        return new ErrorClassificationDecision(code, message, category, severity, retryable, userAction, groupKey, evidence, reason, Hash(canonical));
    }

    public static string NormalizeCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (!CodePattern().IsMatch(normalized))
        {
            throw new ArgumentException("Error code is invalid.", nameof(value));
        }
        return normalized;
    }

    private static string NormalizeSummary(string? value, int maxLength)
    {
        var normalized = (value ?? string.Empty).Trim();
        normalized = string.Join(' ', normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length > maxLength)
        {
            normalized = normalized[..(maxLength - 1)] + "…";
        }
        return normalized;
    }

    private static ClassifiedErrorCategory ClassifyCategory(string text)
    {
        if (ContainsAny(text, "network", "socket", "http", "dns", "timeout", "connection")) return ClassifiedErrorCategory.Network;
        if (ContainsAny(text, "gradle", "compile", "build", "assemble", "linker")) return ClassifiedErrorCategory.Build;
        if (ContainsAny(text, "flutter", "dart", "jdk", "java", "android sdk", "toolchain")) return ClassifiedErrorCategory.Toolchain;
        if (ContainsAny(text, "file", "directory", "path", "disk", "permission", "access denied")) return ClassifiedErrorCategory.Filesystem;
        if (ContainsAny(text, "config", "setting", "manifest", "yaml", "json", "credential", "certificate")) return ClassifiedErrorCategory.Configuration;
        return ClassifiedErrorCategory.Unknown;
    }

    private static ClassifiedErrorSeverity ClassifySeverity(string text, ClassifiedErrorCategory category)
    {
        if (ContainsAny(text, "corrupt", "security", "fatal", "data loss", "signature mismatch")) return ClassifiedErrorSeverity.Critical;
        if (category != ClassifiedErrorCategory.Unknown || ContainsAny(text, "error", "failed", "exception")) return ClassifiedErrorSeverity.Error;
        return ClassifiedErrorSeverity.Warning;
    }

    private static bool ContainsAny(string text, params string[] values)
        => values.Any(value => text.Contains(value, StringComparison.Ordinal));

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
