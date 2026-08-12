using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

internal static class B1150PolicyPrimitives
{
    private static readonly Regex IdentityPattern = new("^[a-z0-9][a-z0-9._-]{0,127}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Identity(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Identity is required.", parameterName);
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (!IdentityPattern.IsMatch(normalized))
        {
            throw new ArgumentException($"Unsafe identity '{value}'.", parameterName);
        }

        return normalized;
    }

    public static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static DateTimeOffset Utc(DateTimeOffset value) => value.ToUniversalTime();
}

public sealed record PortAllocationDecision(
    string Purpose,
    int RequestedPort,
    int AllocatedPort,
    bool RequestedAvailable,
    string ReasonCode,
    string Fingerprint);

public static class PortAllocationSafetyPolicy
{
    public static PortAllocationDecision Evaluate(
        string purpose,
        int requestedPort,
        IEnumerable<int> occupiedPorts,
        IEnumerable<int> reservedPorts,
        int minPort = 1024,
        int maxPort = 65535,
        int maxScan = 4096)
    {
        ArgumentNullException.ThrowIfNull(occupiedPorts);
        ArgumentNullException.ThrowIfNull(reservedPorts);
        var normalizedPurpose = B1150PolicyPrimitives.Identity(purpose, nameof(purpose));
        if (minPort < 1 || maxPort > 65535 || minPort > maxPort)
        {
            throw new ArgumentOutOfRangeException(nameof(minPort), "Port range is invalid.");
        }

        if (requestedPort < minPort || requestedPort > maxPort)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedPort));
        }

        maxScan = Math.Clamp(maxScan, 1, maxPort - minPort + 1);
        var occupied = occupiedPorts.ToArray();
        var reserved = reservedPorts.ToArray();
        ValidatePortSet(occupied, minPort, maxPort, nameof(occupiedPorts));
        ValidatePortSet(reserved, minPort, maxPort, nameof(reservedPorts));

        if (occupied.Distinct().Count() != occupied.Length)
        {
            throw new ArgumentException("Duplicate occupied port detected.", nameof(occupiedPorts));
        }

        if (reserved.Distinct().Count() != reserved.Length)
        {
            throw new ArgumentException("Duplicate reserved port detected.", nameof(reservedPorts));
        }

        var blocked = new HashSet<int>(occupied);
        blocked.UnionWith(reserved);
        var requestedAvailable = !blocked.Contains(requestedPort);
        var allocated = requestedAvailable ? requestedPort : FindAvailable(requestedPort, blocked, minPort, maxPort, maxScan);
        var reason = requestedAvailable
            ? "port-allocation-requested-available"
            : allocated > 0 ? "port-allocation-fallback-selected" : "port-allocation-exhausted";
        var canonical = $"{normalizedPurpose}|{requestedPort}|{allocated}|{requestedAvailable}|{string.Join(',', occupied.OrderBy(x => x))}|{string.Join(',', reserved.OrderBy(x => x))}|{reason}";
        return new PortAllocationDecision(normalizedPurpose, requestedPort, allocated, requestedAvailable, reason, B1150PolicyPrimitives.Hash(canonical));
    }

    private static void ValidatePortSet(IEnumerable<int> ports, int minPort, int maxPort, string parameterName)
    {
        if (ports.Any(port => port < minPort || port > maxPort))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Port set contains an out-of-range value.");
        }
    }

    private static int FindAvailable(int requestedPort, HashSet<int> blocked, int minPort, int maxPort, int maxScan)
    {
        var candidate = requestedPort;
        for (var i = 0; i < maxScan; i++)
        {
            candidate++;
            if (candidate > maxPort)
            {
                candidate = minPort;
            }

            if (!blocked.Contains(candidate))
            {
                return candidate;
            }
        }

        return -1;
    }
}

public sealed record DeviceCapabilityObservation(string Name, int Level);
public sealed record DeviceCapabilityRequirement(string Name, int MinimumLevel);
public sealed record DeviceCapabilityDecision(
    IReadOnlyList<DeviceCapabilityObservation> Observations,
    IReadOnlyList<string> Blockers,
    int Score,
    string ReasonCode,
    string Fingerprint);

public static class DeviceCapabilityRequirementPolicy
{
    public static DeviceCapabilityDecision Evaluate(
        IEnumerable<DeviceCapabilityObservation> observations,
        IEnumerable<DeviceCapabilityRequirement> requirements)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(requirements);
        var normalizedObservations = observations.Select(item =>
        {
            if (item.Level < 0) throw new ArgumentOutOfRangeException(nameof(observations), "Capability level cannot be negative.");
            return new DeviceCapabilityObservation(B1150PolicyPrimitives.Identity(item.Name, nameof(observations)), item.Level);
        }).ToArray();
        if (normalizedObservations.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != normalizedObservations.Length)
        {
            throw new ArgumentException("Duplicate capability observation detected.", nameof(observations));
        }

        var normalizedRequirements = requirements.Select(item => new DeviceCapabilityRequirement(
            B1150PolicyPrimitives.Identity(item.Name, nameof(requirements)),
            Math.Clamp(item.MinimumLevel, 0, 10000))).ToArray();
        if (normalizedRequirements.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != normalizedRequirements.Length)
        {
            throw new ArgumentException("Duplicate capability requirement detected.", nameof(requirements));
        }

        var map = normalizedObservations.ToDictionary(item => item.Name, StringComparer.Ordinal);
        var blockers = new List<string>();
        double scoreTotal = 0;
        foreach (var requirement in normalizedRequirements.OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            if (!map.TryGetValue(requirement.Name, out var observation))
            {
                blockers.Add($"missing:{requirement.Name}");
                continue;
            }

            if (observation.Level < requirement.MinimumLevel)
            {
                blockers.Add($"insufficient:{requirement.Name}:{observation.Level}/{requirement.MinimumLevel}");
            }

            scoreTotal += requirement.MinimumLevel == 0
                ? 1
                : Math.Min(1d, (double)observation.Level / requirement.MinimumLevel);
        }

        var score = normalizedRequirements.Length == 0 ? 100 : (int)Math.Round(scoreTotal * 100 / normalizedRequirements.Length, MidpointRounding.AwayFromZero);
        score = Math.Clamp(score, 0, 100);
        var ordered = normalizedObservations.OrderBy(item => item.Name, StringComparer.Ordinal).ToArray();
        var reason = blockers.Count == 0 ? "device-capabilities-satisfied" : "device-capabilities-blocked";
        var canonical = string.Join("|", ordered.Select(item => $"{item.Name}:{item.Level}")) + "||" + string.Join("|", blockers) + $"|{score}|{reason}";
        return new DeviceCapabilityDecision(ordered, blockers, score, reason, B1150PolicyPrimitives.Hash(canonical));
    }
}

public sealed record ResourceVector(int CpuUnits, long MemoryBytes, long DiskBytes);
public sealed record ResourceReservationDecision(
    string ReservationIdentity,
    ResourceVector Capacity,
    ResourceVector Available,
    ResourceVector Requested,
    int SafetyReservePercent,
    bool Granted,
    string ReasonCode,
    string Fingerprint);

public static class ResourceReservationPolicy
{
    public static ResourceReservationDecision Evaluate(
        string reservationIdentity,
        ResourceVector capacity,
        ResourceVector requested,
        int safetyReservePercent = 10)
    {
        ArgumentNullException.ThrowIfNull(capacity);
        ArgumentNullException.ThrowIfNull(requested);
        var identity = B1150PolicyPrimitives.Identity(reservationIdentity, nameof(reservationIdentity));
        ValidateVector(capacity, nameof(capacity));
        ValidateVector(requested, nameof(requested));
        var reserve = Math.Clamp(safetyReservePercent, 0, 90);
        var available = new ResourceVector(
            capacity.CpuUnits * (100 - reserve) / 100,
            capacity.MemoryBytes / 100 * (100 - reserve),
            capacity.DiskBytes / 100 * (100 - reserve));
        var granted = requested.CpuUnits <= available.CpuUnits && requested.MemoryBytes <= available.MemoryBytes && requested.DiskBytes <= available.DiskBytes;
        var reason = granted ? "resource-reservation-granted" : "resource-reservation-rejected";
        var canonical = $"{identity}|{capacity.CpuUnits}:{capacity.MemoryBytes}:{capacity.DiskBytes}|{available.CpuUnits}:{available.MemoryBytes}:{available.DiskBytes}|{requested.CpuUnits}:{requested.MemoryBytes}:{requested.DiskBytes}|{reserve}|{granted}|{reason}";
        return new ResourceReservationDecision(identity, capacity, available, requested, reserve, granted, reason, B1150PolicyPrimitives.Hash(canonical));
    }

    private static void ValidateVector(ResourceVector vector, string parameterName)
    {
        if (vector.CpuUnits < 0 || vector.MemoryBytes < 0 || vector.DiskBytes < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Resource values cannot be negative.");
        }
    }
}

public sealed record ArtifactSignatureEvidenceDecision(
    string ArtifactIdentity,
    string SignerIdentity,
    string Algorithm,
    string Digest,
    DateTimeOffset SignedAtUtc,
    bool EvidencePresent,
    bool TrustedSigner,
    bool Stale,
    bool Qualified,
    string ReasonCode,
    string Fingerprint);

public static class ArtifactSignatureEvidencePolicy
{
    private static readonly Regex DigestPattern = new("^[0-9a-f]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static ArtifactSignatureEvidenceDecision Evaluate(
        string artifactIdentity,
        string signerIdentity,
        string algorithm,
        string digest,
        DateTimeOffset signedAt,
        DateTimeOffset observedAt,
        bool evidencePresent,
        bool trustedSigner,
        TimeSpan? maxEvidenceAge = null)
    {
        var artifact = B1150PolicyPrimitives.Identity(artifactIdentity, nameof(artifactIdentity));
        var signer = B1150PolicyPrimitives.Identity(signerIdentity, nameof(signerIdentity));
        if (string.IsNullOrWhiteSpace(algorithm)) throw new ArgumentException("Signature algorithm is required.", nameof(algorithm));
        var normalizedAlgorithm = algorithm.Trim().ToLowerInvariant();
        if (normalizedAlgorithm.Any(char.IsControl)) throw new ArgumentException("Signature algorithm contains control characters.", nameof(algorithm));
        var normalizedDigest = (digest ?? string.Empty).Trim().ToLowerInvariant();
        if (!DigestPattern.IsMatch(normalizedDigest)) throw new ArgumentException("Digest must be a SHA-256 hex value.", nameof(digest));

        var signedUtc = B1150PolicyPrimitives.Utc(signedAt);
        var observedUtc = B1150PolicyPrimitives.Utc(observedAt);
        if (signedUtc > observedUtc) throw new ArgumentException("Signing timestamp cannot be after observation time.", nameof(signedAt));
        var ageLimit = maxEvidenceAge ?? TimeSpan.FromDays(30);
        if (ageLimit <= TimeSpan.Zero) ageLimit = TimeSpan.FromMinutes(1);
        if (ageLimit > TimeSpan.FromDays(3650)) ageLimit = TimeSpan.FromDays(3650);
        var stale = observedUtc - signedUtc > ageLimit;
        var qualified = evidencePresent && trustedSigner && !stale;
        var reason = !evidencePresent ? "signature-evidence-missing"
            : !trustedSigner ? "signature-signer-untrusted"
            : stale ? "signature-evidence-stale"
            : "signature-evidence-qualified";
        var canonical = $"{artifact}|{signer}|{normalizedAlgorithm}|{normalizedDigest}|{signedUtc:O}|{observedUtc:O}|{evidencePresent}|{trustedSigner}|{stale}|{reason}";
        return new ArtifactSignatureEvidenceDecision(artifact, signer, normalizedAlgorithm, normalizedDigest, signedUtc, evidencePresent, trustedSigner, stale, qualified, reason, B1150PolicyPrimitives.Hash(canonical));
    }
}

public sealed record TestSuiteAggregateInput(string Name, int Total, int Passed, int Failed, int Skipped, bool Mandatory);
public sealed record TestResultAggregationDecision(
    IReadOnlyList<TestSuiteAggregateInput> Suites,
    long Total,
    long Passed,
    long Failed,
    long Skipped,
    IReadOnlyList<string> MandatoryBlockers,
    int PassPercentage,
    string ReasonCode,
    string Fingerprint);

public static class TestResultAggregationPolicy
{
    public static TestResultAggregationDecision Evaluate(IEnumerable<TestSuiteAggregateInput> suites)
    {
        ArgumentNullException.ThrowIfNull(suites);
        var normalized = suites.Select(item =>
        {
            var name = B1150PolicyPrimitives.Identity(item.Name, nameof(suites));
            if (item.Total < 0 || item.Passed < 0 || item.Failed < 0 || item.Skipped < 0)
                throw new ArgumentOutOfRangeException(nameof(suites), "Test counts cannot be negative.");
            if (item.Passed + item.Failed + item.Skipped != item.Total)
                throw new ArgumentException($"Suite '{name}' counts do not add up to total.", nameof(suites));
            return item with { Name = name };
        }).ToArray();
        if (normalized.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != normalized.Length)
            throw new ArgumentException("Duplicate test suite detected.", nameof(suites));

        var ordered = normalized.OrderBy(item => item.Name, StringComparer.Ordinal).ToArray();
        var total = ordered.Sum(item => (long)item.Total);
        var passed = ordered.Sum(item => (long)item.Passed);
        var failed = ordered.Sum(item => (long)item.Failed);
        var skipped = ordered.Sum(item => (long)item.Skipped);
        var blockers = ordered.Where(item => item.Mandatory && item.Failed > 0).Select(item => $"failed:{item.Name}:{item.Failed}").ToArray();
        var passPercentage = total == 0 ? 100 : (int)Math.Round((double)passed * 100 / total, MidpointRounding.AwayFromZero);
        passPercentage = Math.Clamp(passPercentage, 0, 100);
        var reason = blockers.Length == 0 ? "test-aggregate-qualified" : "test-aggregate-blocked";
        var canonical = string.Join("|", ordered.Select(item => $"{item.Name}:{item.Total}:{item.Passed}:{item.Failed}:{item.Skipped}:{item.Mandatory}")) + $"||{total}:{passed}:{failed}:{skipped}:{passPercentage}:{reason}";
        return new TestResultAggregationDecision(ordered, total, passed, failed, skipped, blockers, passPercentage, reason, B1150PolicyPrimitives.Hash(canonical));
    }
}

public sealed record RecoveryCheckpoint(string Identity, int Sequence, DateTimeOffset CapturedAt, bool Verified);
public sealed record SessionRecoveryDecision(
    string SessionIdentity,
    IReadOnlyList<RecoveryCheckpoint> Checkpoints,
    RecoveryCheckpoint? SelectedCheckpoint,
    int ReplayDistance,
    string ReasonCode,
    string Fingerprint);

public static class SessionRecoveryCheckpointPolicy
{
    public static SessionRecoveryDecision Evaluate(string sessionIdentity, IEnumerable<RecoveryCheckpoint> checkpoints)
    {
        ArgumentNullException.ThrowIfNull(checkpoints);
        var session = B1150PolicyPrimitives.Identity(sessionIdentity, nameof(sessionIdentity));
        var normalized = checkpoints.Select(item =>
        {
            var identity = B1150PolicyPrimitives.Identity(item.Identity, nameof(checkpoints));
            if (item.Sequence < 0) throw new ArgumentOutOfRangeException(nameof(checkpoints), "Checkpoint sequence cannot be negative.");
            return item with { Identity = identity, CapturedAt = B1150PolicyPrimitives.Utc(item.CapturedAt) };
        }).ToArray();
        if (normalized.Select(item => item.Identity).Distinct(StringComparer.Ordinal).Count() != normalized.Length)
            throw new ArgumentException("Duplicate checkpoint identity detected.", nameof(checkpoints));
        if (normalized.Select(item => item.Sequence).Distinct().Count() != normalized.Length)
            throw new ArgumentException("Duplicate checkpoint sequence detected.", nameof(checkpoints));

        var ordered = normalized.OrderBy(item => item.Sequence).ThenBy(item => item.Identity, StringComparer.Ordinal).ToArray();
        var selected = ordered.Where(item => item.Verified).OrderByDescending(item => item.Sequence).ThenByDescending(item => item.CapturedAt).ThenBy(item => item.Identity, StringComparer.Ordinal).FirstOrDefault();
        var highestSequence = ordered.Length == 0 ? -1 : ordered[^1].Sequence;
        var replayDistance = selected is null ? Math.Max(0, highestSequence + 1) : Math.Max(0, highestSequence - selected.Sequence);
        var reason = selected is null ? "session-recovery-checkpoint-missing" : "session-recovery-checkpoint-selected";
        var canonical = session + "|" + string.Join("|", ordered.Select(item => $"{item.Identity}:{item.Sequence}:{item.CapturedAt:O}:{item.Verified}")) + $"|selected:{selected?.Identity ?? "none"}|replay:{replayDistance}|{reason}";
        return new SessionRecoveryDecision(session, ordered, selected, replayDistance, reason, B1150PolicyPrimitives.Hash(canonical));
    }
}

public sealed record BuildQueueItem(string Identity, string Owner, int Priority, DateTimeOffset EnqueuedAt, bool Exclusive);
public sealed record RankedBuildQueueItem(BuildQueueItem Item, int EffectivePriority);
public sealed record BuildQueueFairnessDecision(IReadOnlyList<RankedBuildQueueItem> RankedItems, string ReasonCode, string Fingerprint);

public static class BuildQueueFairnessPolicy
{
    public static BuildQueueFairnessDecision Evaluate(
        IEnumerable<BuildQueueItem> items,
        DateTimeOffset observedAt,
        TimeSpan? agingInterval = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        var observedUtc = B1150PolicyPrimitives.Utc(observedAt);
        var interval = agingInterval ?? TimeSpan.FromMinutes(5);
        if (interval <= TimeSpan.Zero) interval = TimeSpan.FromSeconds(1);
        var normalized = items.Select(item => item with
        {
            Identity = B1150PolicyPrimitives.Identity(item.Identity, nameof(items)),
            Owner = B1150PolicyPrimitives.Identity(item.Owner, nameof(items)),
            Priority = Math.Clamp(item.Priority, 0, 100),
            EnqueuedAt = B1150PolicyPrimitives.Utc(item.EnqueuedAt)
        }).ToArray();
        if (normalized.Select(item => item.Identity).Distinct(StringComparer.Ordinal).Count() != normalized.Length)
            throw new ArgumentException("Duplicate build queue item detected.", nameof(items));

        var ranked = normalized.Select(item =>
        {
            var age = observedUtc > item.EnqueuedAt ? observedUtc - item.EnqueuedAt : TimeSpan.Zero;
            var boost = Math.Clamp((int)(age.Ticks / interval.Ticks), 0, 50);
            return new RankedBuildQueueItem(item, Math.Clamp(item.Priority + boost, 0, 150));
        }).OrderByDescending(item => item.Item.Exclusive)
          .ThenByDescending(item => item.EffectivePriority)
          .ThenBy(item => item.Item.EnqueuedAt)
          .ThenBy(item => item.Item.Identity, StringComparer.Ordinal)
          .ToArray();
        var reason = ranked.Length == 0 ? "build-queue-empty" : "build-queue-ranked";
        var canonical = string.Join("|", ranked.Select(item => $"{item.Item.Identity}:{item.Item.Owner}:{item.Item.Priority}:{item.EffectivePriority}:{item.Item.EnqueuedAt:O}:{item.Item.Exclusive}")) + $"|{reason}";
        return new BuildQueueFairnessDecision(ranked, reason, B1150PolicyPrimitives.Hash(canonical));
    }
}

public sealed record EndpointQuorumProbe(string Identity, bool Healthy, int LatencyMs, bool Mandatory);
public sealed record EndpointQuorumDecision(
    IReadOnlyList<EndpointQuorumProbe> Probes,
    IReadOnlyList<EndpointQuorumProbe> FallbackOrder,
    int RequiredQuorum,
    int HealthyCount,
    bool QuorumMet,
    IReadOnlyList<string> Blockers,
    string ReasonCode,
    string Fingerprint);

public static class EndpointQuorumFallbackPolicy
{
    public static EndpointQuorumDecision Evaluate(IEnumerable<EndpointQuorumProbe> probes, int requiredQuorum)
    {
        ArgumentNullException.ThrowIfNull(probes);
        var normalized = probes.Select(item =>
        {
            var identity = B1150PolicyPrimitives.Identity(item.Identity, nameof(probes));
            if (item.LatencyMs < 0) throw new ArgumentOutOfRangeException(nameof(probes), "Endpoint latency cannot be negative.");
            return item with { Identity = identity };
        }).ToArray();
        if (normalized.Select(item => item.Identity).Distinct(StringComparer.Ordinal).Count() != normalized.Length)
            throw new ArgumentException("Duplicate endpoint probe detected.", nameof(probes));

        var ordered = normalized.OrderBy(item => item.Identity, StringComparer.Ordinal).ToArray();
        var quorum = ordered.Length == 0 ? 0 : Math.Clamp(requiredQuorum, 1, ordered.Length);
        var healthy = ordered.Count(item => item.Healthy);
        var fallback = ordered.Where(item => item.Healthy).OrderBy(item => item.LatencyMs).ThenBy(item => item.Identity, StringComparer.Ordinal).ToArray();
        var blockers = ordered.Where(item => item.Mandatory && !item.Healthy).Select(item => $"mandatory-unhealthy:{item.Identity}").ToArray();
        var quorumMet = healthy >= quorum;
        var reason = quorumMet && blockers.Length == 0 ? "endpoint-quorum-healthy" : "endpoint-quorum-degraded";
        var canonical = string.Join("|", ordered.Select(item => $"{item.Identity}:{item.Healthy}:{item.LatencyMs}:{item.Mandatory}")) + $"|q:{quorum}|h:{healthy}|b:{string.Join(',', blockers)}|{reason}";
        return new EndpointQuorumDecision(ordered, fallback, quorum, healthy, quorumMet, blockers, reason, B1150PolicyPrimitives.Hash(canonical));
    }
}

public sealed record ReleaseRollbackEligibilityDecision(
    string RollbackIdentity,
    string SourceVersion,
    string TargetVersion,
    bool Eligible,
    IReadOnlyList<string> Blockers,
    string ReasonCode,
    string Fingerprint);

public static class ReleaseRollbackEligibilityPolicy
{
    private static readonly Regex VersionPattern = new("^v?(?<major>0|[1-9][0-9]*)\\.(?<minor>0|[1-9][0-9]*)\\.(?<patch>0|[1-9][0-9]*)(?<suffix>-[0-9a-z.-]+)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static ReleaseRollbackEligibilityDecision Evaluate(
        string rollbackIdentity,
        string sourceVersion,
        string targetVersion,
        bool artifactVerified,
        bool backupAvailable,
        bool schemaRollbackCompatible)
    {
        var identity = B1150PolicyPrimitives.Identity(rollbackIdentity, nameof(rollbackIdentity));
        var source = NormalizeVersion(sourceVersion, nameof(sourceVersion));
        var target = NormalizeVersion(targetVersion, nameof(targetVersion));
        if (string.Equals(source, target, StringComparison.Ordinal))
            throw new ArgumentException("Rollback source and target versions must differ.", nameof(targetVersion));
        var blockers = new List<string>();
        if (!artifactVerified) blockers.Add("artifact-unverified");
        if (!backupAvailable) blockers.Add("backup-missing");
        if (!schemaRollbackCompatible) blockers.Add("schema-incompatible");
        var eligible = blockers.Count == 0;
        var reason = eligible ? "release-rollback-eligible" : "release-rollback-blocked";
        var canonical = $"{identity}|{source}|{target}|{artifactVerified}|{backupAvailable}|{schemaRollbackCompatible}|{string.Join(',', blockers)}|{reason}";
        return new ReleaseRollbackEligibilityDecision(identity, source, target, eligible, blockers, reason, B1150PolicyPrimitives.Hash(canonical));
    }

    private static string NormalizeVersion(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Release version is required.", parameterName);
        var match = VersionPattern.Match(value.Trim());
        if (!match.Success) throw new ArgumentException($"Invalid semantic version '{value}'.", parameterName);
        return $"{match.Groups["major"].Value}.{match.Groups["minor"].Value}.{match.Groups["patch"].Value}{match.Groups["suffix"].Value.ToLowerInvariant()}";
    }
}

public sealed record SupportBundleEntry(string Identity, string Category, bool Present, bool Sensitive, bool Redacted);
public sealed record SupportBundleCompletenessDecision(
    IReadOnlyList<SupportBundleEntry> Entries,
    IReadOnlyList<string> MissingCategories,
    IReadOnlyList<string> Blockers,
    int Score,
    bool Complete,
    string ReasonCode,
    string Fingerprint);

public static class SupportBundleCompletenessPolicy
{
    public static SupportBundleCompletenessDecision Evaluate(
        IEnumerable<SupportBundleEntry> entries,
        IEnumerable<string> mandatoryCategories,
        int maxEntries = 512)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(mandatoryCategories);
        maxEntries = Math.Clamp(maxEntries, 1, 4096);
        var normalized = entries.Select(item => item with
        {
            Identity = B1150PolicyPrimitives.Identity(item.Identity, nameof(entries)),
            Category = B1150PolicyPrimitives.Identity(item.Category, nameof(entries))
        }).ToArray();
        if (normalized.Length > maxEntries) throw new ArgumentOutOfRangeException(nameof(entries), $"Support bundle exceeds {maxEntries} entries.");
        if (normalized.Select(item => item.Identity).Distinct(StringComparer.Ordinal).Count() != normalized.Length)
            throw new ArgumentException("Duplicate support bundle entry detected.", nameof(entries));

        var required = mandatoryCategories.Select(category => B1150PolicyPrimitives.Identity(category, nameof(mandatoryCategories))).Distinct(StringComparer.Ordinal).OrderBy(category => category, StringComparer.Ordinal).ToArray();
        var ordered = normalized.OrderBy(item => item.Category, StringComparer.Ordinal).ThenBy(item => item.Identity, StringComparer.Ordinal).ToArray();
        var missing = required.Where(category => !ordered.Any(item => item.Present && item.Category == category)).Select(category => $"missing:{category}").ToArray();
        var sensitive = ordered.Where(item => item.Present && item.Sensitive && !item.Redacted).Select(item => $"unredacted:{item.Identity}").ToArray();
        var blockers = missing.Concat(sensitive).OrderBy(item => item, StringComparer.Ordinal).ToArray();
        var satisfied = required.Length - missing.Length;
        var score = required.Length == 0 ? 100 : (int)Math.Round((double)satisfied * 100 / required.Length, MidpointRounding.AwayFromZero);
        score = Math.Clamp(score, 0, 100);
        var complete = blockers.Length == 0;
        var reason = complete ? "support-bundle-complete" : "support-bundle-incomplete";
        var canonical = string.Join("|", ordered.Select(item => $"{item.Identity}:{item.Category}:{item.Present}:{item.Sensitive}:{item.Redacted}")) + $"|required:{string.Join(',', required)}|blockers:{string.Join(',', blockers)}|score:{score}|{reason}";
        return new SupportBundleCompletenessDecision(ordered, missing, blockers, score, complete, reason, B1150PolicyPrimitives.Hash(canonical));
    }
}
