using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

internal static class B1550PolicyHelpers
{
    private static readonly Regex IdentityPattern = new("^[a-z0-9][a-z0-9._-]{0,127}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex HashPattern = new("^[a-f0-9]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Identity(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Identity is required.", paramName);
        var normalized = value.Trim().ToLowerInvariant();
        if (!IdentityPattern.IsMatch(normalized)) throw new ArgumentException($"Unsafe identity '{value}'.", paramName);
        return normalized;
    }

    public static string HashValue(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("SHA-256 hash is required.", paramName);
        var normalized = value.Trim().ToLowerInvariant();
        if (!HashPattern.IsMatch(normalized)) throw new ArgumentException("Expected a 64-character hexadecimal SHA-256 hash.", paramName);
        return normalized;
    }

    public static string RelativePath(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Relative path is required.", paramName);
        var normalized = value.Trim().Replace('\\', '/');
        if (normalized.StartsWith('/') || Regex.IsMatch(normalized, "^[A-Za-z]:", RegexOptions.CultureInvariant))
            throw new ArgumentException("Rooted paths are not allowed.", paramName);
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
            throw new ArgumentException("Path traversal is not allowed.", paramName);
        return string.Join('/', segments);
    }

    public static DateTimeOffset Utc(DateTimeOffset value) => value.ToUniversalTime();
    public static string Fingerprint(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed record AtomicFileWriteDecision(string OperationIdentity, string TargetPath, string TemporaryPath, string ExpectedHash, bool SameVolume, bool AtomicReplaceEligible, string ReasonCode, string Fingerprint);

public static class AtomicFileWriteSafetyPolicy
{
    public static AtomicFileWriteDecision Evaluate(string operationIdentity, string targetPath, string expectedHash, string targetVolume, string temporaryVolume)
    {
        var operation = B1550PolicyHelpers.Identity(operationIdentity, nameof(operationIdentity));
        var target = B1550PolicyHelpers.RelativePath(targetPath, nameof(targetPath));
        var hash = B1550PolicyHelpers.HashValue(expectedHash, nameof(expectedHash));
        var targetVol = B1550PolicyHelpers.Identity(targetVolume, nameof(targetVolume));
        var tempVol = B1550PolicyHelpers.Identity(temporaryVolume, nameof(temporaryVolume));
        var temporaryPath = $"{target}.tmp.{hash[..12]}";
        var sameVolume = targetVol == tempVol;
        var reason = sameVolume ? "atomic-write-eligible" : "atomic-write-cross-volume";
        var payload = $"{operation}|{target}|{temporaryPath}|{hash}|{targetVol}|{tempVol}|{sameVolume}";
        return new AtomicFileWriteDecision(operation, target, temporaryPath, hash, sameVolume, sameVolume, reason, B1550PolicyHelpers.Fingerprint(payload));
    }
}

public sealed record FileLockWaiter(string OwnerIdentity, TimeSpan WaitAge, int Sequence);
public sealed record FileLockContentionDecision(string ResourceIdentity, string CurrentOwnerIdentity, string RequestedOwnerIdentity, bool HolderExpired, bool ActiveContention, bool Renewal, IReadOnlyList<FileLockWaiter> OrderedWaiters, TimeSpan MaximumWait, string ReasonCode, string Fingerprint);

public static class FileLockContentionPolicy
{
    public static readonly TimeSpan MinWait = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan MinLease = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaxLease = TimeSpan.FromHours(1);

    public static FileLockContentionDecision Evaluate(string resourceIdentity, string currentOwnerIdentity, TimeSpan holderAge, TimeSpan leaseDuration, string requestedOwnerIdentity, IEnumerable<FileLockWaiter> waiters, TimeSpan maximumWait)
    {
        ArgumentNullException.ThrowIfNull(waiters);
        var resource = B1550PolicyHelpers.Identity(resourceIdentity, nameof(resourceIdentity));
        var currentOwner = B1550PolicyHelpers.Identity(currentOwnerIdentity, nameof(currentOwnerIdentity));
        var requestedOwner = B1550PolicyHelpers.Identity(requestedOwnerIdentity, nameof(requestedOwnerIdentity));
        if (holderAge < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(holderAge));
        var lease = leaseDuration < MinLease ? MinLease : leaseDuration > MaxLease ? MaxLease : leaseDuration;
        var boundedWait = maximumWait < MinWait ? MinWait : maximumWait > MaxWait ? MaxWait : maximumWait;
        var ordered = waiters.Select(waiter =>
        {
            ArgumentNullException.ThrowIfNull(waiter);
            if (waiter.WaitAge < TimeSpan.Zero || waiter.Sequence < 0) throw new ArgumentOutOfRangeException(nameof(waiters));
            return waiter with { OwnerIdentity = B1550PolicyHelpers.Identity(waiter.OwnerIdentity, nameof(waiter.OwnerIdentity)) };
        }).OrderByDescending(waiter => waiter.WaitAge).ThenBy(waiter => waiter.Sequence).ThenBy(waiter => waiter.OwnerIdentity, StringComparer.Ordinal).ToArray();
        var expired = holderAge >= lease;
        var renewal = !expired && currentOwner == requestedOwner;
        var contention = !expired && !renewal;
        var reason = expired ? "file-lock-holder-expired" : renewal ? "file-lock-renewal" : "file-lock-contended";
        var payload = $"{resource}|{currentOwner}|{requestedOwner}|{holderAge.Ticks}|{lease.Ticks}|{boundedWait.Ticks}|{expired}|{renewal}|{string.Join(';', ordered.Select(w => $"{w.OwnerIdentity}:{w.WaitAge.Ticks}:{w.Sequence}"))}";
        return new FileLockContentionDecision(resource, currentOwner, requestedOwner, expired, contention, renewal, ordered, boundedWait, reason, B1550PolicyHelpers.Fingerprint(payload));
    }
}

public sealed record CertificateExpiryHorizonDecision(string CertificateIdentity, DateTimeOffset NotBeforeUtc, DateTimeOffset NotAfterUtc, TimeSpan RemainingLifetime, TimeSpan WarningHorizon, string Classification, bool RenewalRequired, string ReasonCode, string Fingerprint);

public static class CertificateExpiryHorizonPolicy
{
    public static readonly TimeSpan MinWarning = TimeSpan.FromDays(1);
    public static readonly TimeSpan MaxWarning = TimeSpan.FromDays(180);

    public static CertificateExpiryHorizonDecision Evaluate(string certificateIdentity, DateTimeOffset notBefore, DateTimeOffset notAfter, DateTimeOffset now, TimeSpan warningHorizon)
    {
        var identity = B1550PolicyHelpers.Identity(certificateIdentity, nameof(certificateIdentity));
        var start = B1550PolicyHelpers.Utc(notBefore);
        var end = B1550PolicyHelpers.Utc(notAfter);
        now = B1550PolicyHelpers.Utc(now);
        if (end <= start) throw new ArgumentException("Certificate validity interval is inverted.", nameof(notAfter));
        var warning = warningHorizon < MinWarning ? MinWarning : warningHorizon > MaxWarning ? MaxWarning : warningHorizon;
        var remaining = end <= now ? TimeSpan.Zero : end - now;
        string classification;
        if (now < start) classification = "not-yet-valid";
        else if (now >= end) classification = "expired";
        else if (remaining <= TimeSpan.FromTicks(Math.Max(1L, warning.Ticks / 4))) classification = "critical";
        else if (remaining <= warning) classification = "warning";
        else classification = "healthy";
        var renewal = classification is "expired" or "critical" or "warning";
        var reason = $"certificate-horizon-{classification}";
        var payload = $"{identity}|{start:O}|{end:O}|{now:O}|{warning.Ticks}|{remaining.Ticks}|{classification}";
        return new CertificateExpiryHorizonDecision(identity, start, end, remaining, warning, classification, renewal, reason, B1550PolicyHelpers.Fingerprint(payload));
    }
}

public sealed record CancellationOperationNode(string Identity, string? ParentIdentity, bool Shielded);
public sealed record CancellationPropagationDecision(string RequestedCancellationIdentity, IReadOnlyList<string> CancelledOperationIds, IReadOnlyList<string> ShieldedOperationIds, string ReasonCode, string Fingerprint);

public static class CancellationPropagationPolicy
{
    public static CancellationPropagationDecision Evaluate(IEnumerable<CancellationOperationNode> nodes, string requestedCancellationIdentity)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var requested = B1550PolicyHelpers.Identity(requestedCancellationIdentity, nameof(requestedCancellationIdentity));
        var normalized = nodes.Select(node =>
        {
            ArgumentNullException.ThrowIfNull(node);
            var identity = B1550PolicyHelpers.Identity(node.Identity, nameof(node.Identity));
            var parent = node.ParentIdentity is null ? null : B1550PolicyHelpers.Identity(node.ParentIdentity, nameof(node.ParentIdentity));
            if (identity == parent) throw new ArgumentException("Operation cannot parent itself.", nameof(nodes));
            return new CancellationOperationNode(identity, parent, node.Shielded);
        }).ToArray();
        if (normalized.GroupBy(node => node.Identity, StringComparer.Ordinal).Any(group => group.Count() > 1)) throw new ArgumentException("Duplicate operation identities are not allowed.", nameof(nodes));
        var map = normalized.ToDictionary(node => node.Identity, StringComparer.Ordinal);
        if (!map.ContainsKey(requested)) throw new ArgumentException("Requested cancellation operation is missing.", nameof(requestedCancellationIdentity));
        foreach (var node in normalized.Where(node => node.ParentIdentity is not null))
            if (!map.ContainsKey(node.ParentIdentity!)) throw new ArgumentException($"Unknown parent '{node.ParentIdentity}'.", nameof(nodes));
        foreach (var node in normalized)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var current = node;
            while (current.ParentIdentity is not null)
            {
                if (!seen.Add(current.Identity)) throw new ArgumentException("Operation tree contains a cycle.", nameof(nodes));
                current = map[current.ParentIdentity];
            }
        }
        var children = normalized.Where(node => node.ParentIdentity is not null).GroupBy(node => node.ParentIdentity!, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.OrderBy(node => node.Identity, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var cancelled = new SortedSet<string>(StringComparer.Ordinal) { requested };
        var shielded = new SortedSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(requested);
        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();
            if (!children.TryGetValue(parent, out var childNodes)) continue;
            foreach (var child in childNodes)
            {
                if (child.Shielded) { shielded.Add(child.Identity); continue; }
                cancelled.Add(child.Identity);
                queue.Enqueue(child.Identity);
            }
        }
        var reason = shielded.Count > 0 ? "cancellation-propagated-with-shields" : "cancellation-propagated";
        var payload = $"{requested}|{string.Join(',', cancelled)}|{string.Join(',', shielded)}";
        return new CancellationPropagationDecision(requested, cancelled.ToArray(), shielded.ToArray(), reason, B1550PolicyHelpers.Fingerprint(payload));
    }
}

public sealed record WorkspaceSnapshot(string Identity, DateTimeOffset CreatedAt, string ContentHash, bool Protected);
public sealed record WorkspaceSnapshotRollbackDecision(string? SelectedSnapshotIdentity, IReadOnlyList<WorkspaceSnapshot> EligibleSnapshots, int ProtectedCount, TimeSpan FutureTolerance, string ReasonCode, string Fingerprint);

public static class WorkspaceSnapshotRollbackPolicy
{
    public static readonly TimeSpan MaxFutureTolerance = TimeSpan.FromMinutes(5);

    public static WorkspaceSnapshotRollbackDecision Evaluate(IEnumerable<WorkspaceSnapshot> snapshots, DateTimeOffset now, TimeSpan futureTolerance)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        now = B1550PolicyHelpers.Utc(now);
        var tolerance = futureTolerance < TimeSpan.Zero ? TimeSpan.Zero : futureTolerance > MaxFutureTolerance ? MaxFutureTolerance : futureTolerance;
        var normalized = snapshots.Select(snapshot =>
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            var created = B1550PolicyHelpers.Utc(snapshot.CreatedAt);
            if (created > now + tolerance) throw new ArgumentException("Snapshot timestamp exceeds future tolerance.", nameof(snapshots));
            return new WorkspaceSnapshot(B1550PolicyHelpers.Identity(snapshot.Identity, nameof(snapshot.Identity)), created, B1550PolicyHelpers.HashValue(snapshot.ContentHash, nameof(snapshot.ContentHash)), snapshot.Protected);
        }).ToArray();
        if (normalized.GroupBy(snapshot => snapshot.Identity, StringComparer.Ordinal).Any(group => group.Count() > 1)) throw new ArgumentException("Duplicate snapshot identities are not allowed.", nameof(snapshots));
        var eligible = normalized.Where(snapshot => snapshot.CreatedAt <= now).OrderByDescending(snapshot => snapshot.CreatedAt).ThenByDescending(snapshot => snapshot.Protected).ThenBy(snapshot => snapshot.Identity, StringComparer.Ordinal).ToArray();
        var selected = eligible.FirstOrDefault()?.Identity;
        var protectedCount = normalized.Count(snapshot => snapshot.Protected);
        var reason = selected is null ? "workspace-rollback-unavailable" : "workspace-rollback-ready";
        var payload = $"{now:O}|{tolerance.Ticks}|{selected}|{protectedCount}|{string.Join(';', eligible.Select(s => $"{s.Identity}:{s.CreatedAt:O}:{s.ContentHash}:{s.Protected}"))}";
        return new WorkspaceSnapshotRollbackDecision(selected, eligible, protectedCount, tolerance, reason, B1550PolicyHelpers.Fingerprint(payload));
    }
}
