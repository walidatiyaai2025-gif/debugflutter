using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

internal static class B1350PolicyHelpers
{
    private static readonly Regex IdentityPattern = new("^[a-z0-9][a-z0-9._-]{0,127}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Sha256Pattern = new("^[a-f0-9]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string NormalizeIdentity(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Identity is required.", paramName);
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (!IdentityPattern.IsMatch(normalized))
        {
            throw new ArgumentException($"Unsafe identity '{value}'.", paramName);
        }

        return normalized;
    }

    public static string NormalizeHash(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("SHA-256 hash is required.", paramName);
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (!Sha256Pattern.IsMatch(normalized))
        {
            throw new ArgumentException("Expected a 64-character hexadecimal SHA-256 hash.", paramName);
        }

        return normalized;
    }

    public static DateTimeOffset Utc(DateTimeOffset value) => value.ToUniversalTime();

    public static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static long SaturatingAdd(long left, long right)
        => left > long.MaxValue - right ? long.MaxValue : left + right;
}

public sealed record OfflineNetworkOperation(string Identity, bool RequiresNetwork, bool CacheOnly, int DeferredSequence);
public sealed record OfflineNetworkModeDecision(string ConnectivityState, IReadOnlyList<string> AllowedOperationIds, IReadOnlyList<string> DeferredOperationIds, bool ReconnectRequired, int DeferredLimit, string ReasonCode, string Fingerprint);

public static class OfflineNetworkModePolicy
{
    private static readonly HashSet<string> AllowedStates = new(StringComparer.Ordinal) { "online", "offline", "degraded" };

    public static OfflineNetworkModeDecision Evaluate(string connectivityState, IEnumerable<OfflineNetworkOperation> operations, int maxDeferredOperations = 100)
    {
        ArgumentNullException.ThrowIfNull(operations);
        var state = (connectivityState ?? string.Empty).Trim().ToLowerInvariant();
        if (!AllowedStates.Contains(state))
        {
            throw new ArgumentException($"Unsupported connectivity state '{connectivityState}'.", nameof(connectivityState));
        }

        var deferredLimit = Math.Clamp(maxDeferredOperations, 1, 1000);
        var normalized = operations.Select(operation =>
        {
            ArgumentNullException.ThrowIfNull(operation);
            if (operation.DeferredSequence < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(operations), "Deferred sequence cannot be negative.");
            }

            return operation with { Identity = B1350PolicyHelpers.NormalizeIdentity(operation.Identity, nameof(operation.Identity)) };
        }).ToArray();

        if (normalized.GroupBy(operation => operation.Identity, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Duplicate network-operation identities are not allowed.", nameof(operations));
        }

        bool ShouldDefer(OfflineNetworkOperation operation)
            => state switch
            {
                "online" => false,
                "offline" => operation.RequiresNetwork && !operation.CacheOnly,
                "degraded" => operation.RequiresNetwork && !operation.CacheOnly,
                _ => true
            };

        var blocked = normalized.Where(ShouldDefer)
            .OrderBy(operation => operation.DeferredSequence)
            .ThenBy(operation => operation.Identity, StringComparer.Ordinal)
            .ToArray();
        var deferred = blocked.Take(deferredLimit)
            .Select(operation => operation.Identity)
            .ToArray();
        var allowed = normalized.Where(operation => !ShouldDefer(operation))
            .OrderBy(operation => operation.Identity, StringComparer.Ordinal)
            .Select(operation => operation.Identity)
            .ToArray();
        var reconnectRequired = state != "online" && blocked.Length > 0;
        var reason = state == "online" ? "network-mode-online" : blocked.Length > 0 ? "network-mode-deferred" : $"network-mode-{state}";
        var payload = $"{state}|{deferredLimit}|{reconnectRequired}|{blocked.Length}|{string.Join(",", allowed)}|{string.Join(",", deferred)}";
        return new OfflineNetworkModeDecision(state, allowed, deferred, reconnectRequired, deferredLimit, reason, B1350PolicyHelpers.Hash(payload));
    }
}

public sealed record PiiEvidenceDecision(string EvidenceIdentity, string Category, IReadOnlyList<string> Classifications, string RedactedText, bool ContainsSensitiveData, int RetainedLength, string ReasonCode, string Fingerprint);

public static class PiiEvidenceClassificationPolicy
{
    private static readonly Regex CredentialPattern = new("(?i)\\b(password|passwd|secret|token|api[_-]?key|credential)\\s*[:=]\\s*[^\\s;,]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EmailPattern = new("(?i)(?<![a-z0-9._%+-])[a-z0-9._%+-]+@[a-z0-9.-]+\\.[a-z]{2,}(?![a-z0-9._%+-])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PhonePattern = new("(?<!\\d)(?:\\+?\\d[\\d -]{7,}\\d)(?!\\d)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Ipv4CandidatePattern = new("(?<!\\d)(?:\\d{1,3}\\.){3}\\d{1,3}(?!\\d)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static PiiEvidenceDecision Evaluate(string evidenceIdentity, string category, string? text, int maxRetainedLength = 2048)
    {
        var identity = B1350PolicyHelpers.NormalizeIdentity(evidenceIdentity, nameof(evidenceIdentity));
        var normalizedCategory = B1350PolicyHelpers.NormalizeIdentity(category, nameof(category));
        var limit = Math.Clamp(maxRetainedLength, 32, 8192);
        var source = text ?? string.Empty;
        var classes = new SortedSet<string>(StringComparer.Ordinal);

        if (CredentialPattern.IsMatch(source)) classes.Add("credential");
        if (EmailPattern.IsMatch(source)) classes.Add("email");
        if (PhonePattern.IsMatch(source)) classes.Add("phone");
        if (Ipv4CandidatePattern.Matches(source).Cast<Match>().Any(match => IPAddress.TryParse(match.Value, out var address) && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)) classes.Add("ip-address");

        var redacted = CredentialPattern.Replace(source, match => match.Groups[1].Value.ToLowerInvariant() + "=[redacted]");
        redacted = EmailPattern.Replace(redacted, "[email]");
        redacted = PhonePattern.Replace(redacted, "[phone]");
        redacted = Ipv4CandidatePattern.Replace(redacted, match => IPAddress.TryParse(match.Value, out var address) && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? "[ip-address]" : match.Value);
        if (redacted.Length > limit)
        {
            redacted = redacted[..limit];
        }

        var containsSensitive = classes.Count > 0;
        var classifications = classes.ToArray();
        var reason = containsSensitive ? "pii-evidence-classified" : "pii-evidence-clear";
        var payload = $"{identity}|{normalizedCategory}|{string.Join(",", classifications)}|{redacted}";
        return new PiiEvidenceDecision(identity, normalizedCategory, classifications, redacted, containsSensitive, redacted.Length, reason, B1350PolicyHelpers.Hash(payload));
    }
}

public sealed record SchemaCompatibilityDecision(string SchemaIdentity, string CurrentVersion, string TargetVersion, string MinimumSupportedVersion, string MaximumSupportedVersion, bool Compatible, string ChangeKind, string ReasonCode, string Fingerprint);

public static class SchemaCompatibilityPolicy
{
    private static readonly Regex SemanticVersionPattern = new("^(0|[1-9]\\d*)\\.(0|[1-9]\\d*)\\.(0|[1-9]\\d*)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static SchemaCompatibilityDecision Evaluate(string schemaIdentity, string currentVersion, string targetVersion, string minimumSupportedVersion, string maximumSupportedVersion)
    {
        var identity = B1350PolicyHelpers.NormalizeIdentity(schemaIdentity, nameof(schemaIdentity));
        var current = Parse(currentVersion, nameof(currentVersion));
        var target = Parse(targetVersion, nameof(targetVersion));
        var minimum = Parse(minimumSupportedVersion, nameof(minimumSupportedVersion));
        var maximum = Parse(maximumSupportedVersion, nameof(maximumSupportedVersion));
        if (minimum > maximum)
        {
            throw new ArgumentException("Minimum supported schema version cannot exceed maximum supported version.");
        }

        var changeKind = target == current ? "same" : target > current ? "upgrade" : "downgrade";
        var inRange = target >= minimum && target <= maximum;
        var sameMajor = target.Major == current.Major;
        var compatible = inRange && sameMajor && target >= current;
        var reason = !inRange ? "schema-version-out-of-range" : !sameMajor ? "schema-major-incompatible" : target < current ? "schema-downgrade-incompatible" : "schema-compatible";
        var payload = $"{identity}|{current}|{target}|{minimum}|{maximum}|{compatible}|{changeKind}|{reason}";
        return new SchemaCompatibilityDecision(identity, Format(current), Format(target), Format(minimum), Format(maximum), compatible, changeKind, reason, B1350PolicyHelpers.Hash(payload));
    }

    private static Version Parse(string value, string paramName)
    {
        var candidate = (value ?? string.Empty).Trim();
        if (!SemanticVersionPattern.IsMatch(candidate) || !Version.TryParse(candidate, out var version))
        {
            throw new ArgumentException($"Invalid semantic schema version '{value}'.", paramName);
        }
        return version;
    }

    private static string Format(Version version) => version.ToString(3);
}

public sealed record DeduplicatedArtifact(string Identity, string ContentHash, long SizeBytes, bool Pinned);
public sealed record ArtifactDuplicateGroup(string ContentHash, string CanonicalArtifactId, IReadOnlyList<string> RemovedArtifactIds, long ReclaimedBytes);
public sealed record ArtifactDeduplicationDecision(IReadOnlyList<ArtifactDuplicateGroup> DuplicateGroups, long TotalReclaimedBytes, string ReasonCode, string Fingerprint);

public static class ArtifactDeduplicationPolicy
{
    public static ArtifactDeduplicationDecision Evaluate(IEnumerable<DeduplicatedArtifact> artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        var normalized = artifacts.Select(artifact =>
        {
            ArgumentNullException.ThrowIfNull(artifact);
            if (artifact.SizeBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(artifacts), "Artifact size cannot be negative.");
            }

            return new DeduplicatedArtifact(
                B1350PolicyHelpers.NormalizeIdentity(artifact.Identity, nameof(artifact.Identity)),
                B1350PolicyHelpers.NormalizeHash(artifact.ContentHash, nameof(artifact.ContentHash)),
                artifact.SizeBytes,
                artifact.Pinned);
        }).ToArray();

        if (normalized.GroupBy(artifact => artifact.Identity, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Duplicate artifact identities are not allowed.", nameof(artifacts));
        }

        long total = 0;
        var groups = normalized.GroupBy(artifact => artifact.ContentHash, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var ordered = group.OrderByDescending(artifact => artifact.Pinned).ThenBy(artifact => artifact.Identity, StringComparer.Ordinal).ToArray();
                var canonical = ordered[0];
                var removed = ordered.Skip(1).OrderBy(artifact => artifact.Identity, StringComparer.Ordinal).ToArray();
                long reclaimed = 0;
                foreach (var artifact in removed)
                {
                    reclaimed = B1350PolicyHelpers.SaturatingAdd(reclaimed, artifact.SizeBytes);
                }
                total = B1350PolicyHelpers.SaturatingAdd(total, reclaimed);
                return new ArtifactDuplicateGroup(group.Key, canonical.Identity, removed.Select(artifact => artifact.Identity).ToArray(), reclaimed);
            }).ToArray();

        var reason = groups.Length == 0 ? "artifact-deduplication-not-needed" : "artifact-deduplication-ready";
        var payload = $"{total}|{string.Join(";", groups.Select(group => $"{group.ContentHash}:{group.CanonicalArtifactId}:{string.Join(",", group.RemovedArtifactIds)}:{group.ReclaimedBytes}"))}";
        return new ArtifactDeduplicationDecision(groups, total, reason, B1350PolicyHelpers.Hash(payload));
    }
}

public sealed record QueueStarvationWorkItem(string Identity, DateTimeOffset EnqueuedAt, int BasePriority, int Attempts);
public sealed record QueueStarvationCandidate(string Identity, DateTimeOffset EnqueuedAt, int BasePriority, int EffectivePriority, TimeSpan WaitAge, bool Starved);
public sealed record QueueStarvationDecision(IReadOnlyList<QueueStarvationCandidate> DispatchOrder, TimeSpan StarvationThreshold, string ReasonCode, string Fingerprint);

public static class QueueStarvationPreventionPolicy
{
    public static QueueStarvationDecision Evaluate(IEnumerable<QueueStarvationWorkItem> workItems, DateTimeOffset now, TimeSpan starvationThreshold)
    {
        ArgumentNullException.ThrowIfNull(workItems);
        now = B1350PolicyHelpers.Utc(now);
        var threshold = starvationThreshold < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : starvationThreshold > TimeSpan.FromHours(24) ? TimeSpan.FromHours(24) : starvationThreshold;
        var candidates = workItems.Select(item =>
        {
            ArgumentNullException.ThrowIfNull(item);
            if (item.Attempts < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(workItems), "Attempt count cannot be negative.");
            }

            var identity = B1350PolicyHelpers.NormalizeIdentity(item.Identity, nameof(item.Identity));
            var enqueuedAt = B1350PolicyHelpers.Utc(item.EnqueuedAt);
            var basePriority = Math.Clamp(item.BasePriority, 0, 100);
            var wait = enqueuedAt >= now ? TimeSpan.Zero : now - enqueuedAt;
            var starved = wait >= threshold;
            var agingSteps = threshold.Ticks == 0 ? 0 : (int)Math.Min(100, wait.Ticks / threshold.Ticks);
            var effective = Math.Min(300, basePriority + agingSteps * 10 + (starved ? 100 : 0));
            return new QueueStarvationCandidate(identity, enqueuedAt, basePriority, effective, wait, starved);
        }).ToArray();

        if (candidates.GroupBy(candidate => candidate.Identity, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Duplicate queued-work identities are not allowed.", nameof(workItems));
        }

        var ordered = candidates.OrderByDescending(candidate => candidate.EffectivePriority)
            .ThenBy(candidate => candidate.EnqueuedAt)
            .ThenBy(candidate => candidate.Identity, StringComparer.Ordinal)
            .ToArray();
        var reason = ordered.Any(candidate => candidate.Starved) ? "queue-starvation-boosted" : "queue-starvation-not-detected";
        var payload = $"{threshold.Ticks}|{string.Join(";", ordered.Select(candidate => $"{candidate.Identity}:{candidate.EffectivePriority}:{candidate.WaitAge.Ticks}:{candidate.Starved}"))}";
        return new QueueStarvationDecision(ordered, threshold, reason, B1350PolicyHelpers.Hash(payload));
    }
}

public sealed record LeaseFencingDecision(string ResourceIdentity, string LeaseOwnerIdentity, long PresentedToken, long HighestToken, bool Allowed, bool Renewal, string ReasonCode, string Fingerprint);

public static class LeaseFencingTokenPolicy
{
    public static LeaseFencingDecision Evaluate(string resourceIdentity, string leaseOwnerIdentity, long presentedToken, long highestToken, string? currentOwnerIdentity)
    {
        var resource = B1350PolicyHelpers.NormalizeIdentity(resourceIdentity, nameof(resourceIdentity));
        var owner = B1350PolicyHelpers.NormalizeIdentity(leaseOwnerIdentity, nameof(leaseOwnerIdentity));
        if (presentedToken < 0 || highestToken < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(presentedToken), "Fencing tokens cannot be negative.");
        }

        var currentOwner = string.IsNullOrWhiteSpace(currentOwnerIdentity) ? null : B1350PolicyHelpers.NormalizeIdentity(currentOwnerIdentity, nameof(currentOwnerIdentity));
        bool allowed;
        bool renewal;
        string reason;
        if (presentedToken < highestToken)
        {
            allowed = false;
            renewal = false;
            reason = "lease-fencing-stale-token";
        }
        else if (presentedToken == highestToken && currentOwner is not null && currentOwner != owner)
        {
            allowed = false;
            renewal = false;
            reason = "lease-fencing-owner-mismatch";
        }
        else
        {
            allowed = true;
            renewal = presentedToken == highestToken && currentOwner == owner;
            reason = renewal ? "lease-fencing-renewal-allowed" : "lease-fencing-token-allowed";
        }

        var payload = $"{resource}|{owner}|{presentedToken}|{highestToken}|{currentOwner}|{allowed}|{renewal}|{reason}";
        return new LeaseFencingDecision(resource, owner, presentedToken, highestToken, allowed, renewal, reason, B1350PolicyHelpers.Hash(payload));
    }
}

public sealed record ReleaseRingPromotionDecision(string ReleaseIdentity, string SourceRing, string TargetRing, TimeSpan SoakDuration, TimeSpan RequiredSoakDuration, bool Eligible, string ReasonCode, string Fingerprint);

public static class ReleaseRingPromotionPolicy
{
    private static readonly string[] Rings = { "internal", "canary", "beta", "stable" };

    public static ReleaseRingPromotionDecision Evaluate(string releaseIdentity, string sourceRing, string targetRing, bool sourceHealthy, TimeSpan soakDuration, TimeSpan requiredSoakDuration, bool hasCriticalRegression, bool allowSkipping = false)
    {
        var identity = B1350PolicyHelpers.NormalizeIdentity(releaseIdentity, nameof(releaseIdentity));
        var source = NormalizeRing(sourceRing, nameof(sourceRing));
        var target = NormalizeRing(targetRing, nameof(targetRing));
        var soak = ClampDuration(soakDuration);
        var required = ClampDuration(requiredSoakDuration);
        var sourceIndex = Array.IndexOf(Rings, source);
        var targetIndex = Array.IndexOf(Rings, target);

        bool eligible;
        string reason;
        if (targetIndex <= sourceIndex)
        {
            eligible = false;
            reason = "release-ring-invalid-progression";
        }
        else if (!allowSkipping && targetIndex != sourceIndex + 1)
        {
            eligible = false;
            reason = "release-ring-skip-blocked";
        }
        else if (!sourceHealthy)
        {
            eligible = false;
            reason = "release-ring-source-unhealthy";
        }
        else if (hasCriticalRegression)
        {
            eligible = false;
            reason = "release-ring-critical-regression";
        }
        else if (soak < required)
        {
            eligible = false;
            reason = "release-ring-soak-incomplete";
        }
        else
        {
            eligible = true;
            reason = "release-ring-promotion-eligible";
        }

        var payload = $"{identity}|{source}|{target}|{sourceHealthy}|{soak.Ticks}|{required.Ticks}|{hasCriticalRegression}|{allowSkipping}|{eligible}|{reason}";
        return new ReleaseRingPromotionDecision(identity, source, target, soak, required, eligible, reason, B1350PolicyHelpers.Hash(payload));
    }

    private static string NormalizeRing(string value, string paramName)
    {
        var ring = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (!Rings.Contains(ring, StringComparer.Ordinal))
        {
            throw new ArgumentException($"Unsupported release ring '{value}'.", paramName);
        }
        return ring;
    }

    private static TimeSpan ClampDuration(TimeSpan value)
        => value < TimeSpan.Zero ? TimeSpan.Zero : value > TimeSpan.FromDays(30) ? TimeSpan.FromDays(30) : value;
}

public sealed record EnvironmentSetting(string Key, string? Value);
public sealed record EnvironmentDriftFinding(string Key, string Kind, string? ExpectedValue, string? ObservedValue);
public sealed record EnvironmentDriftDecision(IReadOnlyList<EnvironmentDriftFinding> Findings, int IgnoredVolatileKeyCount, bool HasDrift, string ReasonCode, string Fingerprint);

public static class EnvironmentDriftDetectionPolicy
{
    public static EnvironmentDriftDecision Evaluate(IEnumerable<EnvironmentSetting> expected, IEnumerable<EnvironmentSetting> observed, IEnumerable<string>? volatileKeys = null)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(observed);
        var ignored = (volatileKeys ?? Array.Empty<string>()).Select(key => B1350PolicyHelpers.NormalizeIdentity(key, nameof(volatileKeys))).ToHashSet(StringComparer.Ordinal);
        var expectedMap = Normalize(expected, nameof(expected));
        var observedMap = Normalize(observed, nameof(observed));
        var allKeys = expectedMap.Keys.Concat(observedMap.Keys).Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal).ToArray();
        var findings = new List<EnvironmentDriftFinding>();
        var ignoredCount = 0;

        foreach (var key in allKeys)
        {
            if (ignored.Contains(key))
            {
                ignoredCount++;
                continue;
            }

            var hasExpected = expectedMap.TryGetValue(key, out var expectedValue);
            var hasObserved = observedMap.TryGetValue(key, out var observedValue);
            if (hasExpected && !hasObserved)
            {
                findings.Add(new EnvironmentDriftFinding(key, "missing", expectedValue, null));
            }
            else if (!hasExpected && hasObserved)
            {
                findings.Add(new EnvironmentDriftFinding(key, "unexpected", null, observedValue));
            }
            else if (!string.Equals(expectedValue, observedValue, StringComparison.Ordinal))
            {
                findings.Add(new EnvironmentDriftFinding(key, "changed", expectedValue, observedValue));
            }
        }

        var ordered = findings.OrderBy(finding => finding.Key, StringComparer.Ordinal).ThenBy(finding => finding.Kind, StringComparer.Ordinal).ToArray();
        var hasDrift = ordered.Length > 0;
        var reason = hasDrift ? "environment-drift-detected" : "environment-drift-clear";
        var payload = $"{ignoredCount}|{string.Join(";", ordered.Select(finding => $"{finding.Key}:{finding.Kind}:{finding.ExpectedValue}:{finding.ObservedValue}"))}";
        return new EnvironmentDriftDecision(ordered, ignoredCount, hasDrift, reason, B1350PolicyHelpers.Hash(payload));
    }

    private static Dictionary<string, string> Normalize(IEnumerable<EnvironmentSetting> settings, string paramName)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var setting in settings)
        {
            ArgumentNullException.ThrowIfNull(setting);
            var key = B1350PolicyHelpers.NormalizeIdentity(setting.Key, nameof(setting.Key));
            if (!result.TryAdd(key, (setting.Value ?? string.Empty).Trim().Replace("\r\n", "\n", StringComparison.Ordinal)))
            {
                throw new ArgumentException($"Duplicate environment key '{key}'.", paramName);
            }
        }
        return result;
    }
}

public sealed record CommandReplayNonce(string Nonce, DateTimeOffset IssuedAt);
public sealed record CommandReplayProtectionDecision(string CommandIdentity, string Nonce, DateTimeOffset IssuedAt, TimeSpan ReplayWindow, IReadOnlyList<CommandReplayNonce> RetainedHistory, bool Accepted, string ReasonCode, string Fingerprint);

public static class CommandReplayProtectionPolicy
{
    public static CommandReplayProtectionDecision Evaluate(string commandIdentity, string nonce, DateTimeOffset issuedAt, DateTimeOffset now, TimeSpan replayWindow, IEnumerable<CommandReplayNonce> nonceHistory, int maxHistory = 1024)
    {
        ArgumentNullException.ThrowIfNull(nonceHistory);
        var command = B1350PolicyHelpers.NormalizeIdentity(commandIdentity, nameof(commandIdentity));
        var normalizedNonce = B1350PolicyHelpers.NormalizeIdentity(nonce, nameof(nonce));
        issuedAt = B1350PolicyHelpers.Utc(issuedAt);
        now = B1350PolicyHelpers.Utc(now);
        var window = replayWindow < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : replayWindow > TimeSpan.FromHours(24) ? TimeSpan.FromHours(24) : replayWindow;
        var historyLimit = Math.Clamp(maxHistory, 1, 4096);
        var history = nonceHistory.Select(item =>
        {
            ArgumentNullException.ThrowIfNull(item);
            return new CommandReplayNonce(B1350PolicyHelpers.NormalizeIdentity(item.Nonce, nameof(item.Nonce)), B1350PolicyHelpers.Utc(item.IssuedAt));
        }).OrderByDescending(item => item.IssuedAt).ThenBy(item => item.Nonce, StringComparer.Ordinal).Take(historyLimit).ToArray();

        var stale = issuedAt > now || now - issuedAt > window;
        var replayed = history.Any(item => item.Nonce == normalizedNonce);
        var accepted = !stale && !replayed;
        var reason = stale ? "command-replay-stale" : replayed ? "command-replay-duplicate" : "command-replay-accepted";
        var payload = $"{command}|{normalizedNonce}|{issuedAt:O}|{now:O}|{window.Ticks}|{accepted}|{reason}|{string.Join(",", history.Select(item => item.Nonce))}";
        return new CommandReplayProtectionDecision(command, normalizedNonce, issuedAt, window, history, accepted, reason, B1350PolicyHelpers.Hash(payload));
    }
}

public sealed record StorageConsistencyEntry(string Identity, string ExpectedHash, string? ObservedHash);
public sealed record StorageRepairCandidate(string Identity, string Kind, string ExpectedHash, string? ObservedHash);
public sealed record StorageConsistencyDecision(IReadOnlyList<string> ConsistentEntryIds, IReadOnlyList<StorageRepairCandidate> RepairCandidates, bool Consistent, string ReasonCode, string Fingerprint);

public static class StorageConsistencyPolicy
{
    public static StorageConsistencyDecision Evaluate(IEnumerable<StorageConsistencyEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var normalized = entries.Select(entry =>
        {
            ArgumentNullException.ThrowIfNull(entry);
            var identity = B1350PolicyHelpers.NormalizeIdentity(entry.Identity, nameof(entry.Identity));
            var expected = B1350PolicyHelpers.NormalizeHash(entry.ExpectedHash, nameof(entry.ExpectedHash));
            var observed = string.IsNullOrWhiteSpace(entry.ObservedHash) ? null : B1350PolicyHelpers.NormalizeHash(entry.ObservedHash, nameof(entry.ObservedHash));
            return new StorageConsistencyEntry(identity, expected, observed);
        }).ToArray();

        if (normalized.GroupBy(entry => entry.Identity, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Duplicate storage-entry identities are not allowed.", nameof(entries));
        }

        var consistent = normalized.Where(entry => entry.ObservedHash == entry.ExpectedHash).Select(entry => entry.Identity).OrderBy(identity => identity, StringComparer.Ordinal).ToArray();
        var repairs = normalized.Where(entry => entry.ObservedHash != entry.ExpectedHash)
            .Select(entry => new StorageRepairCandidate(entry.Identity, entry.ObservedHash is null ? "missing" : "hash-mismatch", entry.ExpectedHash, entry.ObservedHash))
            .OrderBy(candidate => candidate.Identity, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Kind, StringComparer.Ordinal)
            .ToArray();
        var isConsistent = repairs.Length == 0;
        var reason = isConsistent ? "storage-consistency-valid" : "storage-consistency-repair-required";
        var payload = $"{string.Join(",", consistent)}|{string.Join(";", repairs.Select(candidate => $"{candidate.Identity}:{candidate.Kind}:{candidate.ExpectedHash}:{candidate.ObservedHash}"))}";
        return new StorageConsistencyDecision(consistent, repairs, isConsistent, reason, B1350PolicyHelpers.Hash(payload));
    }
}
