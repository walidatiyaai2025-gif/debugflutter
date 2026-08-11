using System.Security.Cryptography;
using System.Text;

namespace FlutterBuildDoctor.Application.Recovery;

public sealed record RecoveryCheckpoint(
    string Id,
    DateTimeOffset CreatedAt,
    string LastSuccessfulPhase,
    bool Restorable = true);

public sealed record RecoveryCheckpointDecision(
    IReadOnlyList<RecoveryCheckpoint> History,
    RecoveryCheckpoint? Preferred,
    string ReasonCode,
    string Fingerprint);

public static class RecoveryCheckpointPolicy
{
    public const int DefaultMaxHistory = 20;
    public const int MaxHistory = 100;

    public static RecoveryCheckpointDecision Evaluate(
        IEnumerable<RecoveryCheckpoint> checkpoints,
        DateTimeOffset now,
        TimeSpan staleAfter,
        int maxHistory = DefaultMaxHistory)
    {
        ArgumentNullException.ThrowIfNull(checkpoints);
        if (staleAfter <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(staleAfter));
        }

        var normalized = checkpoints.Select(Normalize).ToArray();
        if (normalized.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Length)
        {
            throw new ArgumentException("Duplicate checkpoint identities are not allowed.", nameof(checkpoints));
        }

        var nowUtc = now.ToUniversalTime();
        var limit = Math.Clamp(maxHistory, 1, MaxHistory);
        var history = normalized
            .OrderByDescending(item => item.CreatedAt)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();

        var restorable = history.Where(item => item.Restorable).ToArray();
        var preferred = restorable.FirstOrDefault(item => !IsStale(item, nowUtc, staleAfter));
        var reason = preferred is not null
            ? "checkpoint-ready"
            : history.Length == 0
                ? "no-checkpoints"
                : restorable.Length == 0
                    ? "no-restorable-checkpoint"
                    : "restorable-checkpoints-stale";

        var fingerprint = ComputeFingerprint(history, preferred, reason, nowUtc, staleAfter);
        return new RecoveryCheckpointDecision(history, preferred, reason, fingerprint);
    }

    public static bool IsStale(RecoveryCheckpoint checkpoint, DateTimeOffset now, TimeSpan staleAfter)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (staleAfter <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(staleAfter));
        }

        var age = now.ToUniversalTime() - checkpoint.CreatedAt.ToUniversalTime();
        return age > staleAfter;
    }

    private static RecoveryCheckpoint Normalize(RecoveryCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        var id = NormalizeId(checkpoint.Id);
        var phase = NormalizePhase(checkpoint.LastSuccessfulPhase);
        return checkpoint with
        {
            Id = id,
            CreatedAt = checkpoint.CreatedAt.ToUniversalTime(),
            LastSuccessfulPhase = phase
        };
    }

    private static string NormalizeId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var normalized = id.Trim().ToLowerInvariant();
        if (normalized.Length > 96 || normalized.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Checkpoint identity must be compact and whitespace-free.", nameof(id));
        }

        return normalized;
    }

    private static string NormalizePhase(string phase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);
        return phase.Trim().ToLowerInvariant();
    }

    private static string ComputeFingerprint(
        IEnumerable<RecoveryCheckpoint> history,
        RecoveryCheckpoint? preferred,
        string reason,
        DateTimeOffset nowUtc,
        TimeSpan staleAfter)
    {
        var canonicalHistory = history.Select(item => string.Join(':',
            item.Id,
            item.CreatedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            item.LastSuccessfulPhase,
            item.Restorable));
        var canonical = string.Join('|', canonicalHistory.Prepend(string.Join(':',
            preferred?.Id ?? string.Empty,
            reason,
            nowUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            staleAfter.Ticks)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
