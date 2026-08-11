using System.Security.Cryptography;
using System.Text;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record OperationLease(
    string Identity,
    string Owner,
    DateTimeOffset AcquiredAtUtc,
    TimeSpan Duration)
{
    public DateTimeOffset ExpiresAtUtc => AcquiredAtUtc + Duration;
}

public sealed record LeaseDecision(
    bool Acquired,
    OperationLease Lease,
    bool ExpiredExistingLease,
    string ReasonCode,
    string Fingerprint);

public static class OperationLeasePolicy
{
    public static readonly TimeSpan MinDuration = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(30);

    public static LeaseDecision Acquire(
        string identity,
        string owner,
        DateTimeOffset acquiredAt,
        TimeSpan requestedDuration,
        DateTimeOffset now,
        OperationLease? existing = null)
    {
        var normalizedIdentity = NormalizeToken(identity, nameof(identity));
        var normalizedOwner = NormalizeToken(owner, nameof(owner));
        var acquiredUtc = acquiredAt.ToUniversalTime();
        var nowUtc = now.ToUniversalTime();
        var duration = TimeSpan.FromSeconds(Math.Clamp(
            requestedDuration.TotalSeconds,
            MinDuration.TotalSeconds,
            MaxDuration.TotalSeconds));

        var expiredExisting = existing is not null && nowUtc >= existing.ExpiresAtUtc.ToUniversalTime();
        var conflict = existing is not null && !expiredExisting &&
            !string.Equals(existing.Owner.Trim(), normalizedOwner, StringComparison.OrdinalIgnoreCase);

        var acquired = !conflict;
        var lease = acquired
            ? new OperationLease(normalizedIdentity, normalizedOwner, acquiredUtc, duration)
            : existing!;
        var reason = conflict
            ? "lease-conflict"
            : existing is not null && !expiredExisting
                ? "lease-renewed"
                : expiredExisting
                    ? "lease-reacquired"
                    : "lease-acquired";

        var payload = $"{lease.Identity}|{lease.Owner}|{lease.AcquiredAtUtc:O}|{lease.Duration.TotalSeconds:0}|{acquired}|{reason}";
        return new LeaseDecision(acquired, lease, expiredExisting, reason, Hash(payload));
    }

    private static string NormalizeToken(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Lease token is required.", parameterName);
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > 128 || normalized.Any(char.IsControl))
            throw new ArgumentException("Lease token is invalid.", parameterName);
        return normalized;
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
