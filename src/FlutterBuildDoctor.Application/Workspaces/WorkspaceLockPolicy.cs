using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Workspaces;

public sealed record WorkspaceLock(
    string LockId,
    string OwnerId,
    string WorkspacePath,
    DateTimeOffset AcquiredAt,
    TimeSpan Lease);

public sealed record WorkspaceLockDecision(
    bool Allowed,
    bool ExistingOwner,
    bool ExistingExpired,
    WorkspaceLock Requested,
    string ReasonCode,
    string Fingerprint);

public static partial class WorkspaceLockPolicy
{
    public static readonly TimeSpan MinLease = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan MaxLease = TimeSpan.FromHours(4);

    public static WorkspaceLockDecision Evaluate(
        WorkspaceLock requested,
        WorkspaceLock? existing,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(requested);
        var normalizedNow = now.ToUniversalTime();
        var normalizedRequested = Normalize(requested);
        var normalizedExisting = existing is null ? null : Normalize(existing);

        var existingExpired = normalizedExisting is not null && IsExpired(normalizedExisting, normalizedNow);
        var existingOwner = normalizedExisting is not null
            && !existingExpired
            && string.Equals(normalizedExisting.OwnerId, normalizedRequested.OwnerId, StringComparison.Ordinal);
        var conflict = normalizedExisting is not null && !existingExpired && !existingOwner;

        var reason = conflict ? "active-lock-conflict"
            : existingOwner ? "active-owner-lock"
            : existingExpired ? "expired-lock-replace"
            : "lock-available";

        var canonical = string.Join('|',
            normalizedRequested.LockId,
            normalizedRequested.OwnerId,
            normalizedRequested.WorkspacePath.ToUpperInvariant(),
            normalizedRequested.AcquiredAt.ToUniversalTime().ToString("O"),
            normalizedRequested.Lease.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            normalizedExisting?.LockId ?? string.Empty,
            existingExpired,
            existingOwner,
            reason);

        return new WorkspaceLockDecision(!conflict, existingOwner, existingExpired, normalizedRequested, reason, Hash(canonical));
    }

    public static WorkspaceLock Normalize(WorkspaceLock value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var lockId = ValidateIdentity(value.LockId, nameof(value.LockId));
        var ownerId = ValidateIdentity(value.OwnerId, nameof(value.OwnerId));
        var workspacePath = NormalizeWorkspacePath(value.WorkspacePath);
        var acquiredAt = value.AcquiredAt.ToUniversalTime();
        var lease = TimeSpan.FromSeconds(Math.Clamp(value.Lease.TotalSeconds, MinLease.TotalSeconds, MaxLease.TotalSeconds));
        return value with { LockId = lockId, OwnerId = ownerId, WorkspacePath = workspacePath, AcquiredAt = acquiredAt, Lease = lease };
    }

    public static string NormalizeWorkspacePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var trimmed = path.Trim();
        if (!Path.IsPathFullyQualified(trimmed))
        {
            throw new ArgumentException("Workspace path must be fully qualified.", nameof(path));
        }
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(trimmed));
    }

    public static bool IsExpired(WorkspaceLock value, DateTimeOffset now)
        => value.AcquiredAt.ToUniversalTime() + value.Lease <= now.ToUniversalTime();

    private static string ValidateIdentity(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (!IdentityRegex().IsMatch(normalized))
        {
            throw new ArgumentException("Lock identity is invalid.", parameterName);
        }
        return normalized;
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [GeneratedRegex("^[A-Za-z0-9_.:-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentityRegex();
}
