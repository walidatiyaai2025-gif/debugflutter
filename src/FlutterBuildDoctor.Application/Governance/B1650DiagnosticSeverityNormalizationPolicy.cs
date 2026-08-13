using System;
using System.Collections.Generic;
using System.Linq;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record DiagnosticSeverityInput(string Identity, string Label, int Score);
public sealed record DiagnosticSeverityItem(string Identity, string CanonicalSeverity, int Score);
public sealed record DiagnosticSeverityNormalizationDecision(IReadOnlyList<DiagnosticSeverityItem> Diagnostics, string HighestSeverity, string ReasonCode, string Fingerprint);

public static class DiagnosticSeverityNormalizationPolicy
{
    private static readonly Dictionary<string, int> LabelRanks = new(StringComparer.Ordinal)
    {
        ["trace"] = 0,
        ["info"] = 1,
        ["warning"] = 2,
        ["error"] = 3,
        ["critical"] = 4
    };

    public static DiagnosticSeverityNormalizationDecision Evaluate(IEnumerable<DiagnosticSeverityInput> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        var normalized = diagnostics.Select(item =>
        {
            ArgumentNullException.ThrowIfNull(item);
            var identity = B1550PolicyHelpers.Identity(item.Identity, nameof(item.Identity));
            var label = (item.Label ?? string.Empty).Trim().ToLowerInvariant();
            if (!LabelRanks.ContainsKey(label)) throw new ArgumentException("Unsupported diagnostic severity label.", nameof(diagnostics));
            var score = Math.Clamp(item.Score, 0, 100);
            var scoreRank = score >= 90 ? 4 : score >= 70 ? 3 : score >= 40 ? 2 : score >= 10 ? 1 : 0;
            var rank = Math.Max(LabelRanks[label], scoreRank);
            var canonical = rank switch { 4 => "critical", 3 => "error", 2 => "warning", 1 => "info", _ => "trace" };
            return new DiagnosticSeverityItem(identity, canonical, score);
        }).OrderBy(item => item.Identity, StringComparer.Ordinal).ToArray();
        if (normalized.GroupBy(item => item.Identity, StringComparer.Ordinal).Any(group => group.Count() > 1)) throw new ArgumentException("Duplicate diagnostic identities are not allowed.", nameof(diagnostics));
        var highest = normalized.Select(item => item.CanonicalSeverity).OrderByDescending(value => LabelRanks[value]).FirstOrDefault() ?? "trace";
        var payload = $"{highest}|{string.Join(';', normalized.Select(item => $"{item.Identity}:{item.CanonicalSeverity}:{item.Score}"))}";
        return new DiagnosticSeverityNormalizationDecision(normalized, highest, "diagnostic-severity-normalized", B1550PolicyHelpers.Fingerprint(payload));
    }
}
