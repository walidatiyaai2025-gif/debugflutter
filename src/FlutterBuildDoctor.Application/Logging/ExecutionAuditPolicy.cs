using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Logging;

public enum AuditSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Critical = 3
}

public sealed record ExecutionAuditEvent(
    string Name,
    string Message,
    AuditSeverity Severity,
    DateTimeOffset Timestamp);

public sealed record SanitizedAuditEvent(
    string Name,
    string Message,
    AuditSeverity Severity,
    DateTimeOffset TimestampUtc);

public sealed record ExecutionAuditResult(IReadOnlyList<SanitizedAuditEvent> Events, string Fingerprint);

public static partial class ExecutionAuditPolicy
{
    public const int MaxEvents = 500;
    public const int MaxMessageLength = 2048;

    public static ExecutionAuditResult Sanitize(IEnumerable<ExecutionAuditEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var materialized = events.ToList();
        if (materialized.Count > MaxEvents)
        {
            throw new ArgumentOutOfRangeException(nameof(events), "Audit event count exceeds the supported bound.");
        }

        var sanitized = materialized.Select(Normalize)
            .OrderBy(item => item.TimestampUtc)
            .ThenByDescending(item => item.Severity)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();

        var canonical = string.Join('\n', sanitized.Select(item => $"{item.TimestampUtc:O}|{item.Severity}|{item.Name}|{item.Message}"));
        return new ExecutionAuditResult(sanitized, Hash(canonical));
    }

    public static SanitizedAuditEvent Normalize(ExecutionAuditEvent item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var name = NormalizeName(item.Name);
        var message = Redact(item.Message ?? string.Empty);
        if (message.Length > MaxMessageLength)
        {
            message = message[..(MaxMessageLength - 3)] + "...";
        }

        return new SanitizedAuditEvent(name, message, item.Severity, item.Timestamp.ToUniversalTime());
    }

    public static string NormalizeName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (!NameRegex().IsMatch(normalized))
        {
            throw new ArgumentException("Audit event name is invalid.", nameof(value));
        }
        return normalized;
    }

    public static string Redact(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var result = AuthorizationRegex().Replace(value, "$1[REDACTED]");
        result = SecretAssignmentRegex().Replace(result, "$1=[REDACTED]");
        return result;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [GeneratedRegex("^[a-z0-9][a-z0-9_.:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex NameRegex();

    [GeneratedRegex("(?i)(authorization\\s*:\\s*(?:bearer|basic)\\s+)[^\\s,;]+", RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationRegex();

    [GeneratedRegex("(?i)(password|passwd|token|secret|api[-_]?key)\\s*[=:]\\s*[^\\s,;]+", RegexOptions.CultureInvariant)]
    private static partial Regex SecretAssignmentRegex();
}
