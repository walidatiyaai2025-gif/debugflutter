using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record RecoveryActionCandidate(
    string Id,
    bool Destructive,
    bool Reversible,
    bool CreatesCheckpoint,
    bool Confirmed,
    int Order);

public sealed record RecoveryActionPlanDecision(
    IReadOnlyList<RecoveryActionCandidate> Actions,
    int ReversibleCount,
    int DestructiveCount,
    int RiskScore,
    string ReasonCode,
    string Fingerprint);

public static partial class RecoveryActionPlanningPolicy
{
    public const int MaxActions = 64;

    public static RecoveryActionPlanDecision Plan(IEnumerable<RecoveryActionCandidate> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        var input = actions.ToArray();
        if (input.Length > MaxActions) throw new ArgumentOutOfRangeException(nameof(actions), "Recovery action count exceeds the supported bound.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = input.Select(item => Normalize(item, seen))
            .OrderBy(item => item.Order)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();

        var checkpointAvailable = false;
        foreach (var action in ordered)
        {
            if (action.Destructive)
            {
                if (!checkpointAvailable) throw new ArgumentException($"Destructive recovery action '{action.Id}' requires a prior checkpoint.", nameof(actions));
                if (!action.Confirmed) throw new ArgumentException($"Destructive recovery action '{action.Id}' requires confirmation.", nameof(actions));
            }
            if (action.CreatesCheckpoint) checkpointAvailable = true;
        }

        var reversible = ordered.Count(item => item.Reversible);
        var destructive = ordered.Count(item => item.Destructive);
        var risk = Math.Clamp(ordered.Sum(item => (item.Destructive ? 25 : 0) + (!item.Reversible ? 20 : 0)), 0, 100);
        var reason = destructive == 0 ? "recovery-plan-safe" : "recovery-plan-confirmed";
        var canonical = $"{reversible}|{destructive}|{risk}|{reason}\n" + string.Join('\n', ordered.Select(item =>
            $"{item.Order}|{item.Id}|{item.Destructive}|{item.Reversible}|{item.CreatesCheckpoint}|{item.Confirmed}"));
        return new RecoveryActionPlanDecision(ordered, reversible, destructive, risk, reason, Hash(canonical));
    }

    private static RecoveryActionCandidate Normalize(RecoveryActionCandidate item, ISet<string> seen)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Id);
        var id = item.Id.Trim().ToLowerInvariant();
        if (!IdRegex().IsMatch(id)) throw new ArgumentException("Recovery action identity is invalid.", nameof(item));
        if (!seen.Add(id)) throw new ArgumentException($"Duplicate recovery action '{id}'.", nameof(item));
        if (item.Order < 0) throw new ArgumentOutOfRangeException(nameof(item), "Recovery action order cannot be negative.");
        return item with { Id = id };
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdRegex();
}
