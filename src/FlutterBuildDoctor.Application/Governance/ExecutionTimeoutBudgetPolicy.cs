using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record PhaseTimeoutBudget(string Phase, TimeSpan Timeout);

public sealed record ExecutionTimeoutBudgetDecision(
    TimeSpan TotalBudget,
    TimeSpan CleanupReserve,
    TimeSpan CancellationGrace,
    IReadOnlyList<PhaseTimeoutBudget> Phases,
    TimeSpan Remaining,
    bool Exhausted,
    string ReasonCode,
    string Fingerprint);

public static partial class ExecutionTimeoutBudgetPolicy
{
    public static readonly TimeSpan MinTotalBudget = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxTotalBudget = TimeSpan.FromHours(2);
    public static readonly TimeSpan MinPhaseBudget = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaxPhaseBudget = TimeSpan.FromHours(1);
    public static readonly TimeSpan MinCleanupReserve = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaxCleanupReserve = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MinCancellationGrace = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaxCancellationGrace = TimeSpan.FromMinutes(2);

    public static ExecutionTimeoutBudgetDecision Evaluate(
        TimeSpan totalBudget,
        IEnumerable<PhaseTimeoutBudget> phases,
        TimeSpan elapsed,
        TimeSpan cleanupReserve,
        TimeSpan cancellationGrace)
    {
        ArgumentNullException.ThrowIfNull(phases);
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));

        var total = Clamp(totalBudget, MinTotalBudget, MaxTotalBudget);
        var cleanup = Clamp(cleanupReserve, MinCleanupReserve, MaxCleanupReserve);
        var cancellation = Clamp(cancellationGrace, MinCancellationGrace, MaxCancellationGrace);
        if (cleanup + cancellation >= total)
        {
            throw new ArgumentException("Reserved cleanup and cancellation time must leave execution budget available.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = phases.Select(item => NormalizePhase(item, seen)).OrderBy(item => item.Phase, StringComparer.Ordinal).ToArray();
        var availableForPhases = total - cleanup - cancellation;
        var allocated = normalized.Aggregate(TimeSpan.Zero, (sum, item) => sum + item.Timeout);
        if (allocated > availableForPhases)
        {
            throw new ArgumentException("Phase timeout allocation exceeds the executable budget.", nameof(phases));
        }

        var remaining = elapsed >= total ? TimeSpan.Zero : total - elapsed;
        var exhausted = remaining == TimeSpan.Zero;
        var reason = exhausted ? "timeout-budget-exhausted" : "timeout-budget-ready";
        var canonical = $"{total.Ticks}|{cleanup.Ticks}|{cancellation.Ticks}|{elapsed.Ticks}|{remaining.Ticks}|{reason}\n" +
            string.Join('\n', normalized.Select(item => $"{item.Phase}|{item.Timeout.Ticks}"));
        return new ExecutionTimeoutBudgetDecision(total, cleanup, cancellation, normalized, remaining, exhausted, reason, Hash(canonical));
    }

    private static PhaseTimeoutBudget NormalizePhase(PhaseTimeoutBudget item, ISet<string> seen)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Phase);
        var phase = item.Phase.Trim().ToLowerInvariant();
        if (!PhaseRegex().IsMatch(phase)) throw new ArgumentException("Timeout phase identity is invalid.", nameof(item));
        if (!seen.Add(phase)) throw new ArgumentException($"Duplicate timeout phase '{phase}'.", nameof(item));
        return new PhaseTimeoutBudget(phase, Clamp(item.Timeout, MinPhaseBudget, MaxPhaseBudget));
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max)
        => value < min ? min : value > max ? max : value;

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex PhaseRegex();
}
