using System;
using System.Collections.Generic;
using System.Linq;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record StatusSummaryItem(string Identity, string Status, bool Required);
public sealed record StatusSummaryAggregationDecision(int ReadyCount, int WarningCount, int ErrorCount, bool Ready, IReadOnlyList<string> BlockingIds, string ReasonCode, string Fingerprint);

public static class StatusSummaryAggregationPolicy
{
    private static readonly HashSet<string> Statuses = new(StringComparer.Ordinal) { "ready", "warning", "error" };

    public static StatusSummaryAggregationDecision Evaluate(IEnumerable<StatusSummaryItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var normalized = items.Select(item =>
        {
            ArgumentNullException.ThrowIfNull(item);
            var identity = B1550PolicyHelpers.Identity(item.Identity, nameof(item.Identity));
            var status = (item.Status ?? string.Empty).Trim().ToLowerInvariant();
            if (!Statuses.Contains(status)) throw new ArgumentException("Unsupported summary status.", nameof(items));
            return new StatusSummaryItem(identity, status, item.Required);
        }).OrderBy(item => item.Identity, StringComparer.Ordinal).ToArray();
        if (normalized.GroupBy(item => item.Identity, StringComparer.Ordinal).Any(group => group.Count() > 1)) throw new ArgumentException("Duplicate summary identities are not allowed.", nameof(items));
        var blockers = normalized.Where(item => item.Required && item.Status == "error").Select(item => item.Identity).ToArray();
        var ready = blockers.Length == 0;
        var readyCount = normalized.Count(item => item.Status == "ready");
        var warningCount = normalized.Count(item => item.Status == "warning");
        var errorCount = normalized.Count(item => item.Status == "error");
        var reason = ready ? "status-summary-ready" : "status-summary-blocked";
        var payload = $"{readyCount}|{warningCount}|{errorCount}|{ready}|{string.Join(',', blockers)}|{string.Join(';', normalized.Select(item => $"{item.Identity}:{item.Status}:{item.Required}"))}";
        return new StatusSummaryAggregationDecision(readyCount, warningCount, errorCount, ready, blockers, reason, B1550PolicyHelpers.Fingerprint(payload));
    }
}
