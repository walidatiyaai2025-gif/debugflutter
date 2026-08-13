using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

internal static class B1250PolicyHelpers
{
    private static readonly Regex IdentityPattern = new("^[a-z0-9][a-z0-9._-]{0,127}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

    public static DateTimeOffset Utc(DateTimeOffset value) => value.ToUniversalTime();

    public static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed record FeatureFlagInput(string Name, string Environment, bool DefaultEnabled, bool OverrideEnabled, bool ProductionOnly);
public sealed record FeatureFlagState(string Name, string Environment, bool Enabled, bool ProductionOnly);
public sealed record FeatureFlagSafetyDecision(IReadOnlyList<FeatureFlagState> Flags, int EnabledCount, string ReasonCode, string Fingerprint);

public static class FeatureFlagSafetyPolicy
{
    private static readonly HashSet<string> AllowedEnvironments = new(StringComparer.Ordinal) { "development", "test", "staging", "production" };

    public static FeatureFlagSafetyDecision Evaluate(IEnumerable<FeatureFlagInput> flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        var normalized = flags.Select(flag =>
        {
            ArgumentNullException.ThrowIfNull(flag);
            var name = B1250PolicyHelpers.NormalizeIdentity(flag.Name, nameof(flag.Name));
            var environment = (flag.Environment ?? string.Empty).Trim().ToLowerInvariant();
            if (!AllowedEnvironments.Contains(environment))
            {
                throw new ArgumentException($"Unsupported environment '{flag.Environment}'.", nameof(flags));
            }

            if (flag.ProductionOnly && environment != "production" && flag.OverrideEnabled)
            {
                throw new InvalidOperationException($"Production-only flag '{name}' cannot be enabled in '{environment}'.");
            }

            return new FeatureFlagState(name, environment, flag.OverrideEnabled || flag.DefaultEnabled, flag.ProductionOnly);
        }).ToArray();

        if (normalized.Length > 512)
        {
            throw new ArgumentOutOfRangeException(nameof(flags), "Feature flag count exceeds 512.");
        }

        if (normalized.GroupBy(flag => flag.Name, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Duplicate feature flag names are not allowed.", nameof(flags));
        }

        var ordered = normalized.OrderBy(flag => flag.Name, StringComparer.Ordinal).ToArray();
        var enabled = ordered.Count(flag => flag.Enabled);
        var payload = string.Join("\n", ordered.Select(flag => $"{flag.Name}|{flag.Environment}|{flag.Enabled}|{flag.ProductionOnly}"));
        return new FeatureFlagSafetyDecision(ordered, enabled, "feature-flags-valid", B1250PolicyHelpers.Hash(payload));
    }
}

public sealed record DeviceLease(string DeviceId, string OwnerId, DateTimeOffset StartsAt, TimeSpan Duration);
public sealed record DeviceLeaseDecision(string? SelectedDeviceId, IReadOnlyList<string> Conflicts, int ExpiredLeaseCount, string ReasonCode, string Fingerprint);

public static class DeviceReservationLeasePolicy
{
    public static readonly TimeSpan MinDuration = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxDuration = TimeSpan.FromHours(8);

    public static DeviceLeaseDecision Evaluate(IEnumerable<DeviceLease> leases, IEnumerable<string> candidateDeviceIds, string requestedOwnerId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(leases);
        ArgumentNullException.ThrowIfNull(candidateDeviceIds);
        var owner = B1250PolicyHelpers.NormalizeIdentity(requestedOwnerId, nameof(requestedOwnerId));
        now = B1250PolicyHelpers.Utc(now);

        var normalizedLeases = leases.Select(lease =>
        {
            ArgumentNullException.ThrowIfNull(lease);
            var device = B1250PolicyHelpers.NormalizeIdentity(lease.DeviceId, nameof(lease.DeviceId));
            var leaseOwner = B1250PolicyHelpers.NormalizeIdentity(lease.OwnerId, nameof(lease.OwnerId));
            var duration = lease.Duration < MinDuration ? MinDuration : lease.Duration > MaxDuration ? MaxDuration : lease.Duration;
            return new DeviceLease(device, leaseOwner, B1250PolicyHelpers.Utc(lease.StartsAt), duration);
        }).ToArray();

        var candidates = candidateDeviceIds.Select(id => B1250PolicyHelpers.NormalizeIdentity(id, nameof(candidateDeviceIds))).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var active = normalizedLeases.Where(lease => lease.StartsAt <= now && lease.StartsAt + lease.Duration > now).ToArray();
        var expiredCount = normalizedLeases.Count(lease => lease.StartsAt + lease.Duration <= now);
        var conflicts = active.Where(lease => lease.OwnerId != owner).Select(lease => lease.DeviceId).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var selected = candidates.FirstOrDefault(device => !active.Any(lease => lease.DeviceId == device && lease.OwnerId != owner));
        var reason = selected is null ? "device-lease-unavailable" : "device-lease-available";
        var payload = $"{owner}|{now:O}|{selected}|{expiredCount}|{string.Join(',', conflicts)}";
        return new DeviceLeaseDecision(selected, conflicts, expiredCount, reason, B1250PolicyHelpers.Hash(payload));
    }
}

public sealed record NetworkBandwidthDecision(string TransferIdentity, long RemainingBytes, long BandwidthBytesPerSecond, long BurstBytes, TimeSpan ThrottleDelay, bool Exhausted, string ReasonCode, string Fingerprint);

public static class NetworkBandwidthBudgetPolicy
{
    public const long MaxBandwidthBytesPerSecond = 1_073_741_824;
    public const long MaxBurstBytes = 1_073_741_824;
    public static readonly TimeSpan MaxThrottleDelay = TimeSpan.FromMinutes(5);

    public static NetworkBandwidthDecision Evaluate(string transferIdentity, long transferredBytes, long totalBudgetBytes, long bandwidthBytesPerSecond, long burstBytes)
    {
        var identity = B1250PolicyHelpers.NormalizeIdentity(transferIdentity, nameof(transferIdentity));
        if (transferredBytes < 0 || totalBudgetBytes < 0 || bandwidthBytesPerSecond < 0 || burstBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(transferredBytes), "Bandwidth metrics cannot be negative.");
        }

        var rate = Math.Clamp(bandwidthBytesPerSecond, 1, MaxBandwidthBytesPerSecond);
        var burst = Math.Clamp(burstBytes, 0, MaxBurstBytes);
        var ceiling = totalBudgetBytes > long.MaxValue - burst ? long.MaxValue : totalBudgetBytes + burst;
        var remaining = transferredBytes >= ceiling ? 0 : ceiling - transferredBytes;
        var exhausted = remaining == 0;
        var overage = transferredBytes > totalBudgetBytes ? transferredBytes - totalBudgetBytes : 0;
        var delaySeconds = overage == 0 ? 0d : Math.Min(MaxThrottleDelay.TotalSeconds, (double)overage / rate);
        var delay = TimeSpan.FromSeconds(delaySeconds);
        var reason = exhausted ? "bandwidth-budget-exhausted" : delay > TimeSpan.Zero ? "bandwidth-budget-throttled" : "bandwidth-budget-available";
        var payload = $"{identity}|{transferredBytes}|{totalBudgetBytes}|{rate}|{burst}|{remaining}|{delay.Ticks}|{exhausted}";
        return new NetworkBandwidthDecision(identity, remaining, rate, burst, delay, exhausted, reason, B1250PolicyHelpers.Hash(payload));
    }
}

public sealed record ArtifactRetentionItem(string Identity, string ArtifactClass, DateTimeOffset CreatedAt, bool Pinned, bool Active);
public sealed record ArtifactRetentionDecision(IReadOnlyList<ArtifactRetentionItem> PurgeCandidates, int PreservedCount, TimeSpan Retention, string ReasonCode, string Fingerprint);

public static class ArtifactRetentionWindowPolicy
{
    public static ArtifactRetentionDecision Evaluate(IEnumerable<ArtifactRetentionItem> artifacts, DateTimeOffset now, TimeSpan retention)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        now = B1250PolicyHelpers.Utc(now);
        var boundedRetention = retention < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : retention > TimeSpan.FromDays(365) ? TimeSpan.FromDays(365) : retention;
        var normalized = artifacts.Select(item =>
        {
            ArgumentNullException.ThrowIfNull(item);
            return new ArtifactRetentionItem(
                B1250PolicyHelpers.NormalizeIdentity(item.Identity, nameof(item.Identity)),
                B1250PolicyHelpers.NormalizeIdentity(item.ArtifactClass, nameof(item.ArtifactClass)),
                B1250PolicyHelpers.Utc(item.CreatedAt),
                item.Pinned,
                item.Active);
        }).ToArray();

        if (normalized.GroupBy(item => item.Identity, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Duplicate artifact identities are not allowed.", nameof(artifacts));
        }

        var purge = normalized
            .Where(item => !item.Pinned && !item.Active && now - item.CreatedAt >= boundedRetention)
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Identity, StringComparer.Ordinal)
            .ToArray();
        var preserved = normalized.Length - purge.Length;
        var payload = $"{now:O}|{boundedRetention.Ticks}|{string.Join(',', purge.Select(item => item.Identity))}|{preserved}";
        return new ArtifactRetentionDecision(purge, preserved, boundedRetention, "artifact-retention-evaluated", B1250PolicyHelpers.Hash(payload));
    }
}

public sealed record CrashEvidenceDecision(string CrashIdentity, string ExceptionType, DateTimeOffset Timestamp, string RedactedMessage, IReadOnlyList<string> StackFrames, string Signature, string ReasonCode, string Fingerprint);

public static class CrashEvidenceNormalizationPolicy
{
    private static readonly Regex SecretPattern = new("(?i)(password|secret|token|api[_-]?key)\\s*[:=]\\s*[^\\s;]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static CrashEvidenceDecision Evaluate(string crashIdentity, string exceptionType, DateTimeOffset timestamp, string? message, IEnumerable<string> stackFrames, int maxFrames = 128)
    {
        ArgumentNullException.ThrowIfNull(stackFrames);
        var identity = B1250PolicyHelpers.NormalizeIdentity(crashIdentity, nameof(crashIdentity));
        var exception = string.IsNullOrWhiteSpace(exceptionType) ? throw new ArgumentException("Exception type is required.", nameof(exceptionType)) : exceptionType.Trim().ToLowerInvariant();
        timestamp = B1250PolicyHelpers.Utc(timestamp);
        maxFrames = Math.Clamp(maxFrames, 1, 256);
        var redacted = SecretPattern.Replace(message ?? string.Empty, match => match.Groups[1].Value.ToLowerInvariant() + "=[redacted]");
        var frames = stackFrames
            .Where(frame => !string.IsNullOrWhiteSpace(frame))
            .Select(frame => frame.Trim().Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal)
            .Take(maxFrames)
            .ToArray();
        var signature = B1250PolicyHelpers.Hash(exception + "\n" + string.Join("\n", frames));
        var payload = $"{identity}|{exception}|{timestamp:O}|{redacted}|{signature}";
        return new CrashEvidenceDecision(identity, exception, timestamp, redacted, frames, signature, "crash-evidence-normalized", B1250PolicyHelpers.Hash(payload));
    }
}

public sealed record DependencySource(string Identity, string Uri);
public sealed record DependencySourceAllowlistDecision(IReadOnlyList<DependencySource> Sources, string ReasonCode, string Fingerprint);

public static class DependencySourceAllowlistPolicy
{
    public static DependencySourceAllowlistDecision Evaluate(IEnumerable<DependencySource> sources, IEnumerable<string> approvedHosts)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(approvedHosts);
        var hosts = approvedHosts.Select(host => (host ?? string.Empty).Trim().ToLowerInvariant()).Where(host => host.Length > 0).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var normalized = sources.Select(source =>
        {
            ArgumentNullException.ThrowIfNull(source);
            var identity = B1250PolicyHelpers.NormalizeIdentity(source.Identity, nameof(source.Identity));
            if (!System.Uri.TryCreate(source.Uri, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new ArgumentException($"Dependency source '{source.Uri}' must use HTTPS.", nameof(sources));
            }
            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                throw new ArgumentException("Embedded source credentials are not allowed.", nameof(sources));
            }
            var host = uri.Host.ToLowerInvariant();
            if (!hosts.Contains(host))
            {
                throw new InvalidOperationException($"Dependency source host '{host}' is not approved.");
            }
            var builder = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Host = host, Port = uri.IsDefaultPort ? -1 : uri.Port };
            var normalizedUri = builder.Uri.AbsoluteUri.TrimEnd('/');
            return new DependencySource(identity, normalizedUri);
        }).ToArray();

        if (normalized.GroupBy(source => source.Identity, StringComparer.Ordinal).Any(group => group.Count() > 1) || normalized.GroupBy(source => source.Uri, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Duplicate dependency sources are not allowed.", nameof(sources));
        }

        var ordered = normalized.OrderBy(source => source.Identity, StringComparer.Ordinal).ToArray();
        var payload = string.Join("\n", ordered.Select(source => $"{source.Identity}|{source.Uri}"));
        return new DependencySourceAllowlistDecision(ordered, "dependency-sources-approved", B1250PolicyHelpers.Hash(payload));
    }
}

public sealed record ProcessTreeNode(int ProcessId, int? ParentProcessId, bool Owned);
public sealed record ProcessTreeOwnershipDecision(int TargetProcessId, int RootProcessId, IReadOnlyList<int> TerminableProcessIds, string ReasonCode, string Fingerprint);

public static class ProcessTreeOwnershipPolicy
{
    public static ProcessTreeOwnershipDecision Evaluate(IEnumerable<ProcessTreeNode> nodes, int targetProcessId)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        if (targetProcessId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetProcessId));
        }

        var array = nodes.ToArray();
        if (array.Any(node => node.ProcessId <= 0 || node.ParentProcessId <= 0))
        {
            throw new ArgumentException("Process identifiers must be positive.", nameof(nodes));
        }
        if (array.GroupBy(node => node.ProcessId).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Duplicate process identifiers are not allowed.", nameof(nodes));
        }
        var map = array.ToDictionary(node => node.ProcessId);
        foreach (var node in array)
        {
            if (node.ParentProcessId == node.ProcessId)
            {
                throw new ArgumentException("A process cannot parent itself.", nameof(nodes));
            }
            if (node.ParentProcessId.HasValue && !map.ContainsKey(node.ParentProcessId.Value))
            {
                throw new ArgumentException($"Unknown parent process '{node.ParentProcessId.Value}'.", nameof(nodes));
            }
        }
        if (!map.ContainsKey(targetProcessId))
        {
            throw new ArgumentException("Target process is missing from the process tree.", nameof(targetProcessId));
        }

        foreach (var node in array)
        {
            var seen = new HashSet<int>();
            var current = node;
            while (current.ParentProcessId.HasValue)
            {
                if (!seen.Add(current.ProcessId))
                {
                    throw new ArgumentException("Process tree contains a cycle.", nameof(nodes));
                }
                current = map[current.ParentProcessId.Value];
            }
        }

        var target = map[targetProcessId];
        var root = target;
        while (root.ParentProcessId.HasValue)
        {
            root = map[root.ParentProcessId.Value];
        }

        bool IsDescendantOfTarget(ProcessTreeNode node)
        {
            var current = node;
            while (current.ParentProcessId.HasValue)
            {
                if (current.ParentProcessId.Value == targetProcessId)
                {
                    return true;
                }
                current = map[current.ParentProcessId.Value];
            }
            return node.ProcessId == targetProcessId;
        }

        var terminable = array.Where(node => node.Owned && IsDescendantOfTarget(node)).Select(node => node.ProcessId).OrderBy(id => id).ToArray();
        var payload = $"{targetProcessId}|{root.ProcessId}|{string.Join(',', terminable)}";
        return new ProcessTreeOwnershipDecision(targetProcessId, root.ProcessId, terminable, "process-tree-ownership-valid", B1250PolicyHelpers.Hash(payload));
    }
}

public sealed record ChildRetryScope(string Identity, int RequestedAttempts, int UsedAttempts);
public sealed record NestedRetryBudgetDecision(string ScopeIdentity, int TotalAttempts, int UsedAttempts, int RemainingAttempts, IReadOnlyList<ChildRetryScope> Children, string ReasonCode, string Fingerprint);

public static class NestedRetryBudgetPolicy
{
    public static NestedRetryBudgetDecision Evaluate(string scopeIdentity, int totalAttempts, int usedAttempts, IEnumerable<ChildRetryScope> children)
    {
        ArgumentNullException.ThrowIfNull(children);
        var identity = B1250PolicyHelpers.NormalizeIdentity(scopeIdentity, nameof(scopeIdentity));
        if (totalAttempts < 0 || usedAttempts < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalAttempts));
        }
        var total = Math.Clamp(totalAttempts, 1, 100);
        if (usedAttempts > total)
        {
            throw new ArgumentException("Used attempts cannot exceed total attempts.", nameof(usedAttempts));
        }

        var normalized = children.Select(child =>
        {
            ArgumentNullException.ThrowIfNull(child);
            var childIdentity = B1250PolicyHelpers.NormalizeIdentity(child.Identity, nameof(child.Identity));
            if (child.RequestedAttempts < 0 || child.UsedAttempts < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(children));
            }
            var requested = Math.Clamp(child.RequestedAttempts, 0, total);
            if (child.UsedAttempts > requested)
            {
                throw new ArgumentException("Child used attempts cannot exceed its allocation.", nameof(children));
            }
            return new ChildRetryScope(childIdentity, requested, child.UsedAttempts);
        }).OrderBy(child => child.Identity, StringComparer.Ordinal).ToArray();

        if (normalized.GroupBy(child => child.Identity, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Duplicate retry child scopes are not allowed.", nameof(children));
        }
        var parentRemaining = total - usedAttempts;
        var allocated = normalized.Sum(child => child.RequestedAttempts);
        if (allocated > parentRemaining)
        {
            throw new InvalidOperationException("Child retry allocation exceeds the parent retry budget.");
        }
        var remaining = parentRemaining - allocated;
        var reason = parentRemaining == 0 ? "nested-retry-budget-exhausted" : "nested-retry-budget-available";
        var payload = $"{identity}|{total}|{usedAttempts}|{remaining}|{string.Join(';', normalized.Select(child => $"{child.Identity}:{child.RequestedAttempts}:{child.UsedAttempts}"))}";
        return new NestedRetryBudgetDecision(identity, total, usedAttempts, remaining, normalized, reason, B1250PolicyHelpers.Hash(payload));
    }
}

public sealed record TestFlakeObservation(string TestIdentity, string SuiteIdentity, int RunCount, int PassedCount, int FailedCount, bool Mandatory);
public sealed record TestFlakeState(string TestIdentity, string SuiteIdentity, double FailureRate, string Classification, bool Mandatory);
public sealed record TestFlakeClassificationDecision(IReadOnlyList<TestFlakeState> Tests, int IntermittentCount, int FailingCount, string ReasonCode, string Fingerprint);

public static class TestFlakeClassificationPolicy
{
    public static TestFlakeClassificationDecision Evaluate(IEnumerable<TestFlakeObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        var states = observations.Select(observation =>
        {
            ArgumentNullException.ThrowIfNull(observation);
            var test = B1250PolicyHelpers.NormalizeIdentity(observation.TestIdentity, nameof(observation.TestIdentity));
            var suite = B1250PolicyHelpers.NormalizeIdentity(observation.SuiteIdentity, nameof(observation.SuiteIdentity));
            if (observation.RunCount < 0 || observation.PassedCount < 0 || observation.FailedCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(observations));
            }
            if (observation.PassedCount + observation.FailedCount != observation.RunCount)
            {
                throw new ArgumentException("Test pass/fail counts must equal run count.", nameof(observations));
            }
            var rate = observation.RunCount == 0 ? 0d : (double)observation.FailedCount / observation.RunCount;
            var classification = observation.FailedCount == 0 ? "stable" : observation.Mandatory ? "failing-mandatory" : rate >= 0.5d ? "failing" : "intermittent";
            return new TestFlakeState(test, suite, rate, classification, observation.Mandatory);
        }).ToArray();

        if (states.GroupBy(state => $"{state.SuiteIdentity}/{state.TestIdentity}", StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Duplicate test observations are not allowed.", nameof(observations));
        }

        static int Rank(string classification) => classification switch { "failing-mandatory" => 3, "failing" => 2, "intermittent" => 1, _ => 0 };
        var ordered = states.OrderByDescending(state => Rank(state.Classification)).ThenBy(state => state.SuiteIdentity, StringComparer.Ordinal).ThenBy(state => state.TestIdentity, StringComparer.Ordinal).ToArray();
        var intermittent = ordered.Count(state => state.Classification == "intermittent");
        var failing = ordered.Count(state => state.Classification is "failing" or "failing-mandatory");
        var payload = string.Join("\n", ordered.Select(state => $"{state.SuiteIdentity}|{state.TestIdentity}|{state.FailureRate:F6}|{state.Classification}|{state.Mandatory}"));
        return new TestFlakeClassificationDecision(ordered, intermittent, failing, "test-flake-classified", B1250PolicyHelpers.Hash(payload));
    }
}

public sealed record ReleaseEvidenceEntry(string Identity, string Category, bool Passed, bool Mandatory);
public sealed record ReleaseEvidenceCompletenessDecision(IReadOnlyList<ReleaseEvidenceEntry> Evidence, IReadOnlyList<string> Blockers, int Score, bool Complete, string ReasonCode, string Fingerprint);

public static class ReleaseEvidenceCompletenessPolicy
{
    public static ReleaseEvidenceCompletenessDecision Evaluate(IEnumerable<ReleaseEvidenceEntry> evidence, IEnumerable<string> requiredCategories, int maxEntries = 256)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(requiredCategories);
        maxEntries = Math.Clamp(maxEntries, 1, 1024);
        var normalized = evidence.Select(entry =>
        {
            ArgumentNullException.ThrowIfNull(entry);
            return new ReleaseEvidenceEntry(
                B1250PolicyHelpers.NormalizeIdentity(entry.Identity, nameof(entry.Identity)),
                B1250PolicyHelpers.NormalizeIdentity(entry.Category, nameof(entry.Category)),
                entry.Passed,
                entry.Mandatory);
        }).ToArray();
        if (normalized.Length > maxEntries)
        {
            throw new ArgumentOutOfRangeException(nameof(evidence));
        }
        if (normalized.GroupBy(entry => entry.Identity, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Duplicate release evidence identities are not allowed.", nameof(evidence));
        }

        var required = requiredCategories.Select(category => B1250PolicyHelpers.NormalizeIdentity(category, nameof(requiredCategories))).Distinct(StringComparer.Ordinal).OrderBy(category => category, StringComparer.Ordinal).ToArray();
        var blockers = new List<string>();
        foreach (var category in required)
        {
            if (!normalized.Any(entry => entry.Category == category))
            {
                blockers.Add("missing:" + category);
            }
        }
        blockers.AddRange(normalized.Where(entry => entry.Mandatory && !entry.Passed).Select(entry => "failed:" + entry.Identity));
        var orderedBlockers = blockers.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var expectedCount = Math.Max(1, required.Length);
        var passedRequired = required.Count(category => normalized.Any(entry => entry.Category == category && entry.Passed));
        var score = (int)Math.Round(100d * passedRequired / expectedCount, MidpointRounding.AwayFromZero);
        score = Math.Clamp(score, 0, 100);
        var complete = orderedBlockers.Length == 0;
        var reason = complete ? "release-evidence-complete" : "release-evidence-incomplete";
        var ordered = normalized.OrderBy(entry => entry.Category, StringComparer.Ordinal).ThenBy(entry => entry.Identity, StringComparer.Ordinal).ToArray();
        var payload = $"{score}|{complete}|{string.Join(',', orderedBlockers)}|{string.Join(';', ordered.Select(entry => $"{entry.Category}:{entry.Identity}:{entry.Passed}:{entry.Mandatory}"))}";
        return new ReleaseEvidenceCompletenessDecision(ordered, orderedBlockers, score, complete, reason, B1250PolicyHelpers.Hash(payload));
    }
}
