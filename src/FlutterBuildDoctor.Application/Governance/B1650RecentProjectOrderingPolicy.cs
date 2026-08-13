using System;
using System.Collections.Generic;
using System.Linq;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record RecentProjectItem(string Identity, DateTimeOffset LastOpenedAt, bool Pinned);
public sealed record RecentProjectOrderingDecision(IReadOnlyList<RecentProjectItem> Selected, int MaximumItems, int PinnedCount, string ReasonCode, string Fingerprint);

public static class RecentProjectOrderingPolicy
{
    public static RecentProjectOrderingDecision Evaluate(IEnumerable<RecentProjectItem> projects, int maximumItems)
    {
        ArgumentNullException.ThrowIfNull(projects);
        var limit = Math.Clamp(maximumItems, 1, 100);
        var normalized = projects.Select(item =>
        {
            ArgumentNullException.ThrowIfNull(item);
            return new RecentProjectItem(B1550PolicyHelpers.Identity(item.Identity, nameof(item.Identity)), B1550PolicyHelpers.Utc(item.LastOpenedAt), item.Pinned);
        }).ToArray();
        if (normalized.GroupBy(item => item.Identity, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new ArgumentException("Duplicate project identities are not allowed.", nameof(projects));
        var selected = normalized.OrderByDescending(item => item.Pinned).ThenByDescending(item => item.LastOpenedAt).ThenBy(item => item.Identity, StringComparer.Ordinal).Take(limit).ToArray();
        var pinned = selected.Count(item => item.Pinned);
        var reason = normalized.Length <= limit ? "recent-projects-complete" : "recent-projects-trimmed";
        var payload = $"{limit}|{pinned}|{string.Join(';', selected.Select(item => $"{item.Identity}:{item.LastOpenedAt:O}:{item.Pinned}"))}";
        return new RecentProjectOrderingDecision(selected, limit, pinned, reason, B1550PolicyHelpers.Fingerprint(payload));
    }
}
