using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

internal static class B1050PolicyCommon
{
    public static string Token(string? value, string name, int max = 127)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0 || normalized.Length > max || !Regex.IsMatch(normalized, "^[a-z0-9][a-z0-9._;:+-]*$"))
            throw new ArgumentException($"Invalid {name} '{value}'.", name);
        return normalized;
    }

    public static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max)
        => value < min ? min : value > max ? max : value;
}

public sealed record ToolchainComponentVersion(string Identity, string Version, string MinimumVersion, int MaximumMajor);
public sealed record ToolchainCompatibilityFailure(string Identity, string Reason);
public sealed record ToolchainCompatibilityDecision(IReadOnlyList<ToolchainComponentVersion> Components, IReadOnlyList<ToolchainCompatibilityFailure> Failures, bool Compatible, string ReasonCode, string Fingerprint);

public static class ToolchainVersionCompatibilityPolicy
{
    public const int DefaultMaxComponents = 64;
    private static readonly Regex VersionPattern = new("^v?(\\d+)\\.(\\d+)\\.(\\d+)(?:[-+][0-9A-Za-z.-]+)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static ToolchainCompatibilityDecision Evaluate(IEnumerable<ToolchainComponentVersion> components, int maxComponents = DefaultMaxComponents)
    {
        ArgumentNullException.ThrowIfNull(components);
        maxComponents = Math.Clamp(maxComponents, 1, 256);
        var normalized = components.Select(Normalize).ToArray();
        if (normalized.Length > maxComponents) throw new ArgumentOutOfRangeException(nameof(components));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in normalized) if (!seen.Add(component.Identity)) throw new ArgumentException($"Duplicate toolchain component '{component.Identity}'.", nameof(components));

        var failures = new List<ToolchainCompatibilityFailure>();
        foreach (var component in normalized)
        {
            var current = Version.Parse(component.Version);
            var minimum = Version.Parse(component.MinimumVersion);
            if (component.MaximumMajor < 0) throw new ArgumentOutOfRangeException(nameof(components));
            if (current < minimum) failures.Add(new(component.Identity, "below-minimum"));
            if (current.Major > component.MaximumMajor) failures.Add(new(component.Identity, "above-maximum-major"));
            if (minimum.Major > component.MaximumMajor) failures.Add(new(component.Identity, "incompatible-supported-range"));
        }

        var ordered = normalized.OrderBy(x => x.Identity, StringComparer.Ordinal).ToArray();
        var orderedFailures = failures.OrderBy(x => x.Identity, StringComparer.Ordinal).ThenBy(x => x.Reason, StringComparer.Ordinal).ToArray();
        var compatible = orderedFailures.Length == 0;
        var reason = compatible ? "toolchain-compatible" : "toolchain-incompatible";
        var canonical = string.Join("\n", ordered.Select(x => $"{x.Identity}|{x.Version}|{x.MinimumVersion}|{x.MaximumMajor}")) + "\n--\n" + string.Join("\n", orderedFailures.Select(x => $"{x.Identity}|{x.Reason}"));
        return new(ordered, orderedFailures, compatible, reason, B1050PolicyCommon.Hash(canonical));
    }

    private static ToolchainComponentVersion Normalize(ToolchainComponentVersion value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(B1050PolicyCommon.Token(value.Identity, nameof(value.Identity), 63), NormalizeVersion(value.Version), NormalizeVersion(value.MinimumVersion), value.MaximumMajor);
    }

    private static string NormalizeVersion(string value)
    {
        var match = VersionPattern.Match((value ?? string.Empty).Trim());
        if (!match.Success) throw new ArgumentException($"Invalid semantic version '{value}'.", nameof(value));
        return $"{int.Parse(match.Groups[1].Value)}.{int.Parse(match.Groups[2].Value)}.{int.Parse(match.Groups[3].Value)}";
    }
}

public sealed record SdkPackageState(string Identity, bool Installed, bool LicenseAccepted, bool Mandatory);
public sealed record SdkReadinessDecision(IReadOnlyList<SdkPackageState> Packages, IReadOnlyList<string> Blockers, int Score, bool Ready, string ReasonCode, string Fingerprint);

public static class SdkLicenseReadinessPolicy
{
    public const int DefaultMaxPackages = 128;
    public static SdkReadinessDecision Evaluate(IEnumerable<SdkPackageState> packages, IEnumerable<string>? requiredPackages = null, int maxPackages = DefaultMaxPackages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        maxPackages = Math.Clamp(maxPackages, 1, 512);
        var normalized = packages.Select(x => x with { Identity = B1050PolicyCommon.Token(x.Identity, nameof(x.Identity)) }).ToArray();
        if (normalized.Length > maxPackages) throw new ArgumentOutOfRangeException(nameof(packages));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in normalized) if (!seen.Add(package.Identity)) throw new ArgumentException($"Duplicate SDK package '{package.Identity}'.", nameof(packages));
        var required = (requiredPackages ?? Array.Empty<string>()).Select(x => B1050PolicyCommon.Token(x, nameof(requiredPackages))).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var blockers = new List<string>();
        foreach (var package in normalized)
        {
            var mustHave = package.Mandatory || required.Contains(package.Identity, StringComparer.OrdinalIgnoreCase);
            if (mustHave && !package.Installed) blockers.Add($"missing:{package.Identity}");
            else if (mustHave && !package.LicenseAccepted) blockers.Add($"unlicensed:{package.Identity}");
        }
        foreach (var item in required) if (!seen.Contains(item)) blockers.Add($"missing:{item}");
        var ordered = normalized.OrderBy(x => x.Identity, StringComparer.Ordinal).ToArray();
        var orderedBlockers = blockers.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var mandatoryTotal = Math.Max(1, ordered.Count(x => x.Mandatory || required.Contains(x.Identity, StringComparer.OrdinalIgnoreCase)) + required.Count(x => !seen.Contains(x)));
        var score = Math.Clamp((int)Math.Round(Math.Max(0, mandatoryTotal - orderedBlockers.Length) * 100d / mandatoryTotal), 0, 100);
        var ready = orderedBlockers.Length == 0;
        var canonical = string.Join("\n", ordered.Select(x => $"{x.Identity}|{x.Installed}|{x.LicenseAccepted}|{x.Mandatory}")) + "\n--\n" + string.Join("\n", orderedBlockers);
        return new(ordered, orderedBlockers, score, ready, ready ? "sdk-readiness-ready" : "sdk-readiness-blocked", B1050PolicyCommon.Hash(canonical));
    }
}

public enum EmulatorHealthStatus { Healthy, Booting, Offline, TimedOut }
public sealed record EmulatorBootHealthDecision(string EmulatorIdentity, DateTimeOffset ObservedAtUtc, TimeSpan BootDuration, TimeSpan BootTimeout, bool Online, bool BootCompleted, EmulatorHealthStatus Status, string ReasonCode, string Fingerprint);

public static class EmulatorBootHealthPolicy
{
    public static readonly TimeSpan MinTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaxTimeout = TimeSpan.FromMinutes(15);
    public static EmulatorBootHealthDecision Evaluate(string emulatorIdentity, DateTimeOffset observedAt, TimeSpan bootDuration, TimeSpan bootTimeout, bool online, bool bootCompleted)
    {
        var identity = B1050PolicyCommon.Token(emulatorIdentity, nameof(emulatorIdentity));
        if (bootDuration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(bootDuration));
        var timeout = B1050PolicyCommon.Clamp(bootTimeout, MinTimeout, MaxTimeout);
        var observed = observedAt.ToUniversalTime();
        EmulatorHealthStatus status;
        string reason;
        if (!online) { status = EmulatorHealthStatus.Offline; reason = "emulator-offline"; }
        else if (bootCompleted) { status = EmulatorHealthStatus.Healthy; reason = "emulator-healthy"; }
        else if (bootDuration >= timeout) { status = EmulatorHealthStatus.TimedOut; reason = "emulator-boot-timeout"; }
        else { status = EmulatorHealthStatus.Booting; reason = "emulator-booting"; }
        var canonical = $"{identity}|{observed:O}|{bootDuration.Ticks}|{timeout.Ticks}|{online}|{bootCompleted}|{status}|{reason}";
        return new(identity, observed, bootDuration, timeout, online, bootCompleted, status, reason, B1050PolicyCommon.Hash(canonical));
    }
}

public enum ProcessHeartbeatStatus { Responsive, Stalled, Completed }
public sealed record ProcessHeartbeatDecision(string OperationIdentity, DateTimeOffset ObservedAtUtc, DateTimeOffset LastHeartbeatUtc, TimeSpan HeartbeatTimeout, TimeSpan StallAge, ProcessHeartbeatStatus Status, string ReasonCode, string Fingerprint);

public static class ProcessHeartbeatPolicy
{
    public static readonly TimeSpan MinTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaxTimeout = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan FutureTolerance = TimeSpan.FromSeconds(5);
    public static ProcessHeartbeatDecision Evaluate(string operationIdentity, DateTimeOffset observedAt, DateTimeOffset lastHeartbeat, TimeSpan timeout, bool completed)
    {
        var identity = B1050PolicyCommon.Token(operationIdentity, nameof(operationIdentity));
        var observed = observedAt.ToUniversalTime();
        var heartbeat = lastHeartbeat.ToUniversalTime();
        if (heartbeat - observed > FutureTolerance) throw new ArgumentOutOfRangeException(nameof(lastHeartbeat));
        var normalizedTimeout = B1050PolicyCommon.Clamp(timeout, MinTimeout, MaxTimeout);
        var age = observed > heartbeat ? observed - heartbeat : TimeSpan.Zero;
        var maxAge = TimeSpan.FromTicks(MaxTimeout.Ticks * 100);
        if (age > maxAge) age = maxAge;
        var status = completed ? ProcessHeartbeatStatus.Completed : age > normalizedTimeout ? ProcessHeartbeatStatus.Stalled : ProcessHeartbeatStatus.Responsive;
        var reason = status switch { ProcessHeartbeatStatus.Completed => "process-heartbeat-completed", ProcessHeartbeatStatus.Stalled => "process-heartbeat-stalled", _ => "process-heartbeat-responsive" };
        var canonical = $"{identity}|{observed:O}|{heartbeat:O}|{normalizedTimeout.Ticks}|{age.Ticks}|{status}|{reason}";
        return new(identity, observed, heartbeat, normalizedTimeout, age, status, reason, B1050PolicyCommon.Hash(canonical));
    }
}

public sealed record LogChunk(int Sequence, string Sha256, long ByteLength);
public sealed record LogChunkIntegrityDecision(string StreamIdentity, IReadOnlyList<LogChunk> Chunks, bool StrictSequence, string ReasonCode, string Fingerprint);

public static class LogChunkIntegrityPolicy
{
    public const int DefaultMaxChunks = 4096;
    private static readonly Regex ShaPattern = new("^[0-9a-f]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    public static LogChunkIntegrityDecision Evaluate(string streamIdentity, IEnumerable<LogChunk> chunks, bool strictSequence, int maxChunks = DefaultMaxChunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        var identity = B1050PolicyCommon.Token(streamIdentity, nameof(streamIdentity));
        maxChunks = Math.Clamp(maxChunks, 1, 100000);
        var normalized = chunks.Select(x =>
        {
            if (x.Sequence < 0) throw new ArgumentOutOfRangeException(nameof(chunks));
            if (x.ByteLength < 0) throw new ArgumentOutOfRangeException(nameof(chunks));
            var sha = (x.Sha256 ?? string.Empty).Trim().ToLowerInvariant();
            if (!ShaPattern.IsMatch(sha)) throw new ArgumentException("Invalid chunk SHA-256.", nameof(chunks));
            return new LogChunk(x.Sequence, sha, x.ByteLength);
        }).ToArray();
        if (normalized.Length > maxChunks) throw new ArgumentOutOfRangeException(nameof(chunks));
        var seen = new HashSet<int>();
        foreach (var chunk in normalized) if (!seen.Add(chunk.Sequence)) throw new ArgumentException($"Duplicate chunk sequence {chunk.Sequence}.", nameof(chunks));
        var ordered = normalized.OrderBy(x => x.Sequence).ToArray();
        if (strictSequence && ordered.Length > 0)
        {
            var expected = ordered[0].Sequence;
            foreach (var chunk in ordered) { if (chunk.Sequence != expected) throw new ArgumentException("Log chunk sequence gap detected.", nameof(chunks)); expected++; }
        }
        var canonical = $"{identity}|{strictSequence}\n" + string.Join("\n", ordered.Select(x => $"{x.Sequence}|{x.Sha256}|{x.ByteLength}"));
        return new(identity, ordered, strictSequence, "log-chunk-integrity-valid", B1050PolicyCommon.Hash(canonical));
    }
}

public sealed record BackupEntry(string Identity, DateTimeOffset CreatedAt, long SizeBytes, bool Pinned);
public sealed record BackupRotationDecision(IReadOnlyList<BackupEntry> Retained, IReadOnlyList<BackupEntry> Evicted, long RetainedBytes, string ReasonCode, string Fingerprint);

public static class BackupRotationPolicy
{
    public static BackupRotationDecision Evaluate(IEnumerable<BackupEntry> backups, int maxRetainedCount, long maxRetainedBytes, int minimumNewestToKeep = 1)
    {
        ArgumentNullException.ThrowIfNull(backups);
        maxRetainedCount = Math.Clamp(maxRetainedCount, 1, 10000);
        minimumNewestToKeep = Math.Clamp(minimumNewestToKeep, 0, maxRetainedCount);
        if (maxRetainedBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxRetainedBytes));
        var normalized = backups.Select(x =>
        {
            if (x.SizeBytes < 0) throw new ArgumentOutOfRangeException(nameof(backups));
            return new BackupEntry(B1050PolicyCommon.Token(x.Identity, nameof(x.Identity)), x.CreatedAt.ToUniversalTime(), x.SizeBytes, x.Pinned);
        }).ToArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in normalized) if (!seen.Add(item.Identity)) throw new ArgumentException($"Duplicate backup '{item.Identity}'.", nameof(backups));
        var newestProtected = normalized.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Identity, StringComparer.Ordinal).Take(minimumNewestToKeep).Select(x => x.Identity).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var retained = normalized.ToList();
        var evicted = new List<BackupEntry>();
        while (retained.Count > maxRetainedCount || retained.Sum(x => x.SizeBytes) > maxRetainedBytes)
        {
            var candidate = retained.Where(x => !x.Pinned && !newestProtected.Contains(x.Identity)).OrderBy(x => x.CreatedAt).ThenBy(x => x.Identity, StringComparer.Ordinal).FirstOrDefault();
            if (candidate is null) break;
            retained.Remove(candidate); evicted.Add(candidate);
        }
        var orderedRetained = retained.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Identity, StringComparer.Ordinal).ToArray();
        var orderedEvicted = evicted.OrderBy(x => x.CreatedAt).ThenBy(x => x.Identity, StringComparer.Ordinal).ToArray();
        var bytes = orderedRetained.Sum(x => x.SizeBytes);
        var reason = retained.Count > maxRetainedCount || bytes > maxRetainedBytes ? "backup-rotation-protected-over-limit" : evicted.Count == 0 ? "backup-rotation-noop" : "backup-rotation-evicted";
        var canonical = string.Join("\n", orderedRetained.Select(x => $"R|{x.Identity}|{x.CreatedAt:O}|{x.SizeBytes}|{x.Pinned}")) + "\n" + string.Join("\n", orderedEvicted.Select(x => $"E|{x.Identity}|{x.CreatedAt:O}|{x.SizeBytes}|{x.Pinned}"));
        return new(orderedRetained, orderedEvicted, bytes, reason, B1050PolicyCommon.Hash(canonical));
    }
}

public sealed record CleanupCandidate(string Identity, DateTimeOffset LastUsedAt, long SizeBytes, bool Protected, bool Active);
public sealed record CleanupEligibilityDecision(IReadOnlyList<CleanupCandidate> Eligible, IReadOnlyList<CleanupCandidate> Preserved, long ReclaimableBytes, string ReasonCode, string Fingerprint);

public static class CleanupEligibilityPolicy
{
    public static readonly TimeSpan MinAge = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(365);
    public static CleanupEligibilityDecision Evaluate(IEnumerable<CleanupCandidate> candidates, DateTimeOffset observedAt, TimeSpan minimumAge)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var now = observedAt.ToUniversalTime();
        var age = B1050PolicyCommon.Clamp(minimumAge, MinAge, MaxAge);
        var normalized = candidates.Select(x =>
        {
            if (x.SizeBytes < 0) throw new ArgumentOutOfRangeException(nameof(candidates));
            return new CleanupCandidate(B1050PolicyCommon.Token(x.Identity, nameof(x.Identity)), x.LastUsedAt.ToUniversalTime(), x.SizeBytes, x.Protected, x.Active);
        }).ToArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in normalized) if (!seen.Add(item.Identity)) throw new ArgumentException($"Duplicate cleanup candidate '{item.Identity}'.", nameof(candidates));
        var eligible = normalized.Where(x => !x.Protected && !x.Active && now - x.LastUsedAt >= age).OrderBy(x => x.LastUsedAt).ThenByDescending(x => x.SizeBytes).ThenBy(x => x.Identity, StringComparer.Ordinal).ToArray();
        var eligibleIds = eligible.Select(x => x.Identity).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var preserved = normalized.Where(x => !eligibleIds.Contains(x.Identity)).OrderBy(x => x.Identity, StringComparer.Ordinal).ToArray();
        long reclaimable = 0; foreach (var item in eligible) reclaimable = checked(reclaimable + item.SizeBytes);
        var canonical = $"{now:O}|{age.Ticks}\n" + string.Join("\n", eligible.Select(x => $"E|{x.Identity}|{x.LastUsedAt:O}|{x.SizeBytes}")) + "\n" + string.Join("\n", preserved.Select(x => $"P|{x.Identity}|{x.LastUsedAt:O}|{x.SizeBytes}|{x.Protected}|{x.Active}"));
        return new(eligible, preserved, reclaimable, eligible.Length == 0 ? "cleanup-none-eligible" : "cleanup-eligible", B1050PolicyCommon.Hash(canonical));
    }
}

public sealed record EndpointObservation(string Identity, DateTimeOffset ObservedAt, TimeSpan Latency, double SuccessRatePercent);
public sealed record EndpointHealthResult(string Identity, DateTimeOffset ObservedAtUtc, TimeSpan Latency, double SuccessRatePercent, int LatencyScore, int ReliabilityScore, int HealthScore);
public sealed record EndpointHealthDecision(IReadOnlyList<EndpointHealthResult> Endpoints, string ReasonCode, string Fingerprint);

public static class EndpointHealthScoringPolicy
{
    public static EndpointHealthDecision Evaluate(IEnumerable<EndpointObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        var normalized = observations.Select(x =>
        {
            if (x.Latency < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(observations));
            return new EndpointObservation(B1050PolicyCommon.Token(x.Identity, nameof(x.Identity), 255), x.ObservedAt.ToUniversalTime(), x.Latency, Math.Clamp(x.SuccessRatePercent, 0d, 100d));
        }).ToArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in normalized) if (!seen.Add(item.Identity)) throw new ArgumentException($"Duplicate endpoint '{item.Identity}'.", nameof(observations));
        var results = normalized.Select(x =>
        {
            var ms = x.Latency.TotalMilliseconds;
            var latencyScore = ms <= 100 ? 100 : ms >= 5000 ? 0 : Math.Clamp((int)Math.Round(100 - ((ms - 100) / 4900d * 100d)), 0, 100);
            var reliability = Math.Clamp((int)Math.Round(x.SuccessRatePercent), 0, 100);
            var health = Math.Clamp((int)Math.Round(reliability * 0.7 + latencyScore * 0.3), 0, 100);
            return new EndpointHealthResult(x.Identity, x.ObservedAt.ToUniversalTime(), x.Latency, x.SuccessRatePercent, latencyScore, reliability, health);
        }).OrderByDescending(x => x.HealthScore).ThenBy(x => x.Latency).ThenBy(x => x.Identity, StringComparer.Ordinal).ToArray();
        var reason = results.Length == 0 ? "endpoint-health-empty" : "endpoint-health-ranked";
        var canonical = string.Join("\n", results.Select(x => $"{x.Identity}|{x.ObservedAtUtc:O}|{x.Latency.Ticks}|{x.SuccessRatePercent:F3}|{x.LatencyScore}|{x.ReliabilityScore}|{x.HealthScore}"));
        return new(results, reason, B1050PolicyCommon.Hash(canonical));
    }
}

public sealed record BuildTargetCompatibilityEntry(string Identity, string Platform, string BuildMode, IReadOnlyList<string> RequiredCapabilities);
public sealed record BuildTargetCompatibilityResult(BuildTargetCompatibilityEntry Target, IReadOnlyList<string> MissingCapabilities, bool Supported);
public sealed record BuildTargetCompatibilityDecision(IReadOnlyList<BuildTargetCompatibilityResult> Results, IReadOnlyList<string> Blockers, int SupportedTargetCount, string ReasonCode, string Fingerprint);

public static class BuildTargetCompatibilityMatrixPolicy
{
    public static BuildTargetCompatibilityDecision Evaluate(IEnumerable<BuildTargetCompatibilityEntry> targets, IEnumerable<string> availableCapabilities)
    {
        ArgumentNullException.ThrowIfNull(targets); ArgumentNullException.ThrowIfNull(availableCapabilities);
        var capabilities = availableCapabilities.Select(x => B1050PolicyCommon.Token(x, nameof(availableCapabilities), 63)).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalized = targets.Select(x => new BuildTargetCompatibilityEntry(B1050PolicyCommon.Token(x.Identity, nameof(x.Identity), 63), B1050PolicyCommon.Token(x.Platform, nameof(x.Platform), 63), B1050PolicyCommon.Token(x.BuildMode, nameof(x.BuildMode), 63), (x.RequiredCapabilities ?? Array.Empty<string>()).Select(v => B1050PolicyCommon.Token(v, nameof(x.RequiredCapabilities), 63)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v, StringComparer.Ordinal).ToArray())).ToArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in normalized) { var key = $"{target.Identity}|{target.Platform}|{target.BuildMode}"; if (!seen.Add(key)) throw new ArgumentException($"Duplicate target '{key}'.", nameof(targets)); }
        var results = normalized.Select(target => { var missing = target.RequiredCapabilities.Where(x => !capabilities.Contains(x)).OrderBy(x => x, StringComparer.Ordinal).ToArray(); return new BuildTargetCompatibilityResult(target, missing, missing.Length == 0); }).OrderBy(x => x.Target.Platform, StringComparer.Ordinal).ThenBy(x => x.Target.BuildMode, StringComparer.Ordinal).ThenBy(x => x.Target.Identity, StringComparer.Ordinal).ToArray();
        var blockers = results.Where(x => !x.Supported).Select(x => $"unsupported:{x.Target.Identity}:{string.Join(",", x.MissingCapabilities)}").OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var canonical = string.Join("\n", results.Select(x => $"{x.Target.Identity}|{x.Target.Platform}|{x.Target.BuildMode}|{string.Join(",", x.Target.RequiredCapabilities)}|{x.Supported}|{string.Join(",", x.MissingCapabilities)}"));
        return new(results, blockers, results.Count(x => x.Supported), blockers.Length == 0 ? "build-target-matrix-supported" : "build-target-matrix-blocked", B1050PolicyCommon.Hash(canonical));
    }
}

public sealed record ReleaseQualificationCheck(string Identity, string Category, bool Mandatory, bool Passed, int Weight = 1);
public sealed record ReleaseCandidateQualificationDecision(string CandidateIdentity, IReadOnlyList<ReleaseQualificationCheck> Checks, IReadOnlyList<string> MandatoryBlockers, int Score, bool Qualified, string ReasonCode, string Fingerprint);

public static class ReleaseCandidateQualificationPolicy
{
    public const int DefaultMaxChecks = 128;
    public static ReleaseCandidateQualificationDecision Evaluate(string candidateIdentity, IEnumerable<ReleaseQualificationCheck> checks, IEnumerable<string>? requiredMandatoryChecks = null, int maxChecks = DefaultMaxChecks)
    {
        ArgumentNullException.ThrowIfNull(checks);
        var candidate = B1050PolicyCommon.Token(candidateIdentity, nameof(candidateIdentity), 63);
        maxChecks = Math.Clamp(maxChecks, 1, 1024);
        var normalized = checks.Select(x => x with { Identity = B1050PolicyCommon.Token(x.Identity, nameof(x.Identity), 63), Category = B1050PolicyCommon.Token(x.Category, nameof(x.Category), 63), Weight = Math.Clamp(x.Weight, 1, 100) }).ToArray();
        if (normalized.Length > maxChecks) throw new ArgumentOutOfRangeException(nameof(checks));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var check in normalized) if (!seen.Add(check.Identity)) throw new ArgumentException($"Duplicate qualification check '{check.Identity}'.", nameof(checks));
        var required = (requiredMandatoryChecks ?? Array.Empty<string>()).Select(x => B1050PolicyCommon.Token(x, nameof(requiredMandatoryChecks), 63)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var blockers = new List<string>();
        foreach (var check in normalized) if ((check.Mandatory || required.Contains(check.Identity, StringComparer.OrdinalIgnoreCase)) && !check.Passed) blockers.Add($"failed:{check.Identity}");
        foreach (var requiredIdentity in required) if (!seen.Contains(requiredIdentity)) blockers.Add($"missing:{requiredIdentity}");
        var ordered = normalized.OrderBy(x => x.Category, StringComparer.Ordinal).ThenBy(x => x.Identity, StringComparer.Ordinal).ToArray();
        var orderedBlockers = blockers.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var totalWeight = ordered.Sum(x => x.Weight); var passedWeight = ordered.Where(x => x.Passed).Sum(x => x.Weight);
        var score = totalWeight == 0 ? 100 : Math.Clamp((int)Math.Round(passedWeight * 100d / totalWeight), 0, 100);
        var qualified = orderedBlockers.Length == 0;
        var canonical = $"{candidate}\n" + string.Join("\n", ordered.Select(x => $"{x.Identity}|{x.Category}|{x.Mandatory}|{x.Passed}|{x.Weight}")) + "\n--\n" + string.Join("\n", orderedBlockers);
        return new(candidate, ordered, orderedBlockers, score, qualified, qualified ? "release-candidate-qualified" : "release-candidate-blocked", B1050PolicyCommon.Hash(canonical));
    }
}
