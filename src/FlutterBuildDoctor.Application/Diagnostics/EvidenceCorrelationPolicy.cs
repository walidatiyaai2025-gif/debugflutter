using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Diagnostics;

public enum CorrelatedEvidenceSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Critical = 3
}

public sealed record DiagnosticEvidence(
    string Key,
    string ProblemCode,
    string Message,
    CorrelatedEvidenceSeverity Severity,
    DateTimeOffset CapturedAt);

public sealed record EvidenceGroup(
    string ProblemCode,
    int Occurrences,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc,
    CorrelatedEvidenceSeverity Severity,
    IReadOnlyList<DiagnosticEvidence> Evidence);

public sealed record EvidenceCorrelationResult(IReadOnlyList<EvidenceGroup> Groups, string Fingerprint);

public static partial class EvidenceCorrelationPolicy
{
    public const int MaxEvidencePerGroup = 50;

    public static EvidenceCorrelationResult Correlate(IEnumerable<DiagnosticEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var normalized = evidence.Select(Normalize).ToArray();

        var groups = normalized
            .GroupBy(item => item.ProblemCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildGroup(group.Key, group))
            .OrderByDescending(group => group.Severity)
            .ThenBy(group => group.ProblemCode, StringComparer.Ordinal)
            .ToArray();

        var canonical = string.Join('\n', groups.Select(group =>
            $"{group.ProblemCode}|{group.Occurrences}|{group.FirstSeenUtc:O}|{group.LastSeenUtc:O}|{group.Severity}|" +
            string.Join(';', group.Evidence.Select(item => $"{item.Key}:{item.Message}:{item.CapturedAt:O}"))));

        return new EvidenceCorrelationResult(groups, Hash(canonical));
    }

    public static DiagnosticEvidence Normalize(DiagnosticEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var key = ValidateToken(evidence.Key, nameof(evidence.Key));
        var problemCode = ValidateToken(evidence.ProblemCode, nameof(evidence.ProblemCode)).ToUpperInvariant();
        var message = evidence.Message?.Trim() ?? string.Empty;
        return evidence with
        {
            Key = key,
            ProblemCode = problemCode,
            Message = message,
            CapturedAt = evidence.CapturedAt.ToUniversalTime()
        };
    }

    private static EvidenceGroup BuildGroup(string code, IEnumerable<DiagnosticEvidence> evidence)
    {
        var unique = evidence
            .GroupBy(item => $"{item.Key}\u001f{item.Message}\u001f{item.CapturedAt:O}", StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(item => item.Severity).First())
            .OrderBy(item => item.CapturedAt)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .ToArray();

        var bounded = unique.Take(MaxEvidencePerGroup).ToArray();
        var first = unique.Length == 0 ? DateTimeOffset.UnixEpoch : unique.Min(item => item.CapturedAt);
        var last = unique.Length == 0 ? DateTimeOffset.UnixEpoch : unique.Max(item => item.CapturedAt);
        var severity = unique.Length == 0 ? CorrelatedEvidenceSeverity.Info : unique.Max(item => item.Severity);
        return new EvidenceGroup(code.ToUpperInvariant(), unique.Length, first, last, severity, bounded);
    }

    private static string ValidateToken(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (!TokenRegex().IsMatch(normalized))
        {
            throw new ArgumentException("Evidence key/problem code is invalid.", parameterName);
        }
        return normalized;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [GeneratedRegex("^[A-Za-z0-9_.:-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();
}
