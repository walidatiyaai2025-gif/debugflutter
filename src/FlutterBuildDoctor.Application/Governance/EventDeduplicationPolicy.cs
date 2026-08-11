using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

public enum EventEvidenceSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Critical = 3
}

public sealed record EventEvidence(
    string Identity,
    string Category,
    DateTimeOffset Timestamp,
    string EventFingerprint,
    EventEvidenceSeverity Severity);

public sealed record DeduplicatedEventEvidence(
    string Identity,
    string Category,
    DateTimeOffset FirstSeenUtc,
    string EventFingerprint,
    EventEvidenceSeverity Severity,
    int OccurrenceCount);

public sealed record EventDeduplicationDecision(
    IReadOnlyList<DeduplicatedEventEvidence> Events,
    TimeSpan Window,
    int InputCount,
    string ReasonCode,
    string Fingerprint);

public static class EventDeduplicationPolicy
{
    private static readonly Regex IdentityPattern = new("^[a-z0-9][a-z0-9._:-]{0,127}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex FingerprintPattern = new("^[0-9a-f]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly TimeSpan MinWindow = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxWindow = TimeSpan.FromHours(24);

    public static EventDeduplicationDecision Evaluate(IEnumerable<EventEvidence> events, TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(events);
        var normalizedWindow = Clamp(window, MinWindow, MaxWindow);
        var normalized = events.Select(Normalize).OrderBy(item => item.Timestamp)
            .ThenBy(item => item.Category, StringComparer.Ordinal)
            .ThenBy(item => item.EventFingerprint, StringComparer.Ordinal)
            .ThenBy(item => item.Identity, StringComparer.Ordinal)
            .ToArray();

        var retained = new List<DeduplicatedEventEvidence>(normalized.Length);
        var latestIndexByKey = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var item in normalized)
        {
            var key = item.Category + "|" + item.EventFingerprint;
            if (latestIndexByKey.TryGetValue(key, out var index))
            {
                var existing = retained[index];
                if (item.Timestamp - existing.FirstSeenUtc <= normalizedWindow)
                {
                    var severity = item.Severity > existing.Severity ? item.Severity : existing.Severity;
                    var identity = string.CompareOrdinal(item.Identity, existing.Identity) < 0 ? item.Identity : existing.Identity;
                    retained[index] = existing with
                    {
                        Identity = identity,
                        Severity = severity,
                        OccurrenceCount = existing.OccurrenceCount + 1
                    };
                    continue;
                }
            }

            latestIndexByKey[key] = retained.Count;
            retained.Add(new DeduplicatedEventEvidence(
                item.Identity,
                item.Category,
                item.Timestamp,
                item.EventFingerprint,
                item.Severity,
                1));
        }

        var ordered = retained.OrderBy(item => item.FirstSeenUtc)
            .ThenBy(item => item.Category, StringComparer.Ordinal)
            .ThenBy(item => item.EventFingerprint, StringComparer.Ordinal)
            .ThenBy(item => item.Identity, StringComparer.Ordinal)
            .ToArray();
        var reason = ordered.Length < normalized.Length ? "events-deduplicated" : "events-unique";
        var canonical = string.Join("\n", ordered.Select(item =>
            $"{item.Identity}|{item.Category}|{item.FirstSeenUtc:O}|{item.EventFingerprint}|{item.Severity}|{item.OccurrenceCount}"));
        return new EventDeduplicationDecision(ordered, normalizedWindow, normalized.Length, reason, Hash($"{normalizedWindow.Ticks}\n{canonical}"));
    }

    private static EventEvidence Normalize(EventEvidence item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var identity = NormalizeIdentity(item.Identity, "event identity");
        var category = NormalizeIdentity(item.Category, "event category");
        var fingerprint = (item.EventFingerprint ?? string.Empty).Trim().ToLowerInvariant();
        if (!FingerprintPattern.IsMatch(fingerprint))
        {
            throw new ArgumentException("Event fingerprint must be a 64-character SHA-256 hex value.", nameof(item));
        }

        if (!Enum.IsDefined(item.Severity))
        {
            throw new ArgumentOutOfRangeException(nameof(item), "Event severity is invalid.");
        }

        return new EventEvidence(identity, category, item.Timestamp.ToUniversalTime(), fingerprint, item.Severity);
    }

    private static string NormalizeIdentity(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{label} is required.", nameof(value));
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (!IdentityPattern.IsMatch(normalized))
        {
            throw new ArgumentException($"Unsafe {label} '{value}'.", nameof(value));
        }

        return normalized;
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max)
        => value < min ? min : value > max ? max : value;

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
