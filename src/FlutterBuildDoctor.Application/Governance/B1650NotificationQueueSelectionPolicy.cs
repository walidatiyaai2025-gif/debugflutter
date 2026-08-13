using System;
using System.Collections.Generic;
using System.Linq;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record NotificationQueueItem(string Identity, int Priority, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, bool Mandatory);
public sealed record NotificationQueueSelectionDecision(IReadOnlyList<NotificationQueueItem> Selected, IReadOnlyList<string> ExpiredIds, int MaximumVisible, string ReasonCode, string Fingerprint);

public static class NotificationQueueSelectionPolicy
{
    public static NotificationQueueSelectionDecision Evaluate(IEnumerable<NotificationQueueItem> items, DateTimeOffset now, int maximumVisible)
    {
        ArgumentNullException.ThrowIfNull(items);
        now = B1550PolicyHelpers.Utc(now);
        var limit = Math.Clamp(maximumVisible, 1, 50);
        var normalized = items.Select(item =>
        {
            ArgumentNullException.ThrowIfNull(item);
            var identity = B1550PolicyHelpers.Identity(item.Identity, nameof(item.Identity));
            var created = B1550PolicyHelpers.Utc(item.CreatedAt);
            var expires = item.ExpiresAt is null ? null : B1550PolicyHelpers.Utc(item.ExpiresAt.Value);
            if (expires is not null && expires < created) throw new ArgumentException("Notification expiration cannot precede creation.", nameof(items));
            return new NotificationQueueItem(identity, Math.Clamp(item.Priority, 0, 100), created, expires, item.Mandatory);
        }).ToArray();
        if (normalized.GroupBy(item => item.Identity, StringComparer.Ordinal).Any(group => group.Count() > 1)) throw new ArgumentException("Duplicate notification identities are not allowed.", nameof(items));
        var expired = normalized.Where(item => item.ExpiresAt is not null && item.ExpiresAt <= now).Select(item => item.Identity).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var selected = normalized.Where(item => item.ExpiresAt is null || item.ExpiresAt > now)
            .OrderByDescending(item => item.Mandatory)
            .ThenByDescending(item => item.Priority)
            .ThenBy(item => item.CreatedAt)
            .ThenBy(item => item.Identity, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
        var reason = selected.Length < normalized.Length - expired.Length ? "notification-queue-trimmed" : "notification-queue-ready";
        var payload = $"{now:O}|{limit}|{string.Join(',', expired)}|{string.Join(';', selected.Select(item => $"{item.Identity}:{item.Priority}:{item.CreatedAt:O}:{item.Mandatory}"))}";
        return new NotificationQueueSelectionDecision(selected, expired, limit, reason, B1550PolicyHelpers.Fingerprint(payload));
    }
}
