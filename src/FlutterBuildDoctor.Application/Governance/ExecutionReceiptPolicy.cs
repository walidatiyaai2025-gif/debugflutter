using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

public enum ExecutionReceiptPhaseStatus
{
    Success = 0,
    Failure = 1,
    Cancelled = 2
}

public sealed record ExecutionReceiptPhase(
    string Name,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    ExecutionReceiptPhaseStatus Status);

public sealed record NormalizedExecutionReceiptPhase(
    string Name,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    TimeSpan Duration,
    ExecutionReceiptPhaseStatus Status);

public sealed record ExecutionReceiptDecision(
    string Identity,
    IReadOnlyList<NormalizedExecutionReceiptPhase> Phases,
    string PhaseSummary,
    string ReasonCode,
    string Fingerprint);

public static partial class ExecutionReceiptPolicy
{
    public const int MaxPhases = 64;

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentityPattern();

    public static ExecutionReceiptDecision Evaluate(string identity, IEnumerable<ExecutionReceiptPhase> phases)
    {
        var normalizedIdentity = NormalizeIdentity(identity);
        ArgumentNullException.ThrowIfNull(phases);
        var input = phases.ToArray();
        if (input.Length is < 1 or > MaxPhases)
        {
            throw new ArgumentOutOfRangeException(nameof(phases), "Receipt phase count is outside the supported bound.");
        }

        var normalized = new List<NormalizedExecutionReceiptPhase>(input.Length);
        DateTimeOffset? previousCompleted = null;
        foreach (var phase in input)
        {
            ArgumentNullException.ThrowIfNull(phase);
            var name = NormalizeIdentity(phase.Name);
            if (!Enum.IsDefined(phase.Status))
            {
                throw new ArgumentOutOfRangeException(nameof(phases), "Receipt phase status is invalid.");
            }
            var started = phase.StartedAt.ToUniversalTime();
            var completed = phase.CompletedAt.ToUniversalTime();
            if (completed < started)
            {
                throw new ArgumentException("Receipt phase duration cannot be negative.", nameof(phases));
            }
            if (previousCompleted is not null && started < previousCompleted.Value)
            {
                throw new ArgumentException("Receipt phases must be monotonic and non-overlapping.", nameof(phases));
            }
            normalized.Add(new NormalizedExecutionReceiptPhase(name, started, completed, completed - started, phase.Status));
            previousCompleted = completed;
        }

        var summary = string.Join(" > ", normalized.Select(phase => $"{phase.Name}:{phase.Status.ToString().ToLowerInvariant()}:{phase.Duration.TotalMilliseconds:0}"));
        var canonical = string.Join('\n', normalized.Select(phase => $"{phase.Name}|{phase.StartedAtUtc:O}|{phase.CompletedAtUtc:O}|{(int)phase.Status}"));
        return new ExecutionReceiptDecision(normalizedIdentity, normalized, summary, "execution-receipt-valid", Hash($"{normalizedIdentity}|{canonical}"));
    }

    public static string NormalizeIdentity(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (!IdentityPattern().IsMatch(normalized))
        {
            throw new ArgumentException("Receipt identity is invalid.", nameof(value));
        }
        return normalized;
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
