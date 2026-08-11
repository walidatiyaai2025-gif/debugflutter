using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Reliability;

public enum OrchestrationPhaseState
{
    Missing,
    Pending,
    Passed,
    Failed,
    Skipped
}

public sealed record OrchestrationPhaseEvidence(
    string Name,
    OrchestrationPhaseState State,
    bool Required,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? CompletedAt = null,
    int Weight = 1);

public sealed record OrchestrationPhaseResult(
    string Name,
    OrchestrationPhaseState State,
    bool Required,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    TimeSpan Duration,
    int Weight,
    bool Blocker);

public sealed record OrchestrationQualityResult(
    IReadOnlyList<OrchestrationPhaseResult> Phases,
    int BlockerCount,
    int RequiredCompletionRate,
    int QualityScore,
    bool Successful,
    string Fingerprint);

public static partial class OrchestrationQualityPolicy
{
    public static OrchestrationQualityResult Evaluate(
        IEnumerable<string> requiredPhaseNames,
        IEnumerable<OrchestrationPhaseEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(requiredPhaseNames);
        ArgumentNullException.ThrowIfNull(evidence);

        var required = requiredPhaseNames.Select(NormalizeName).ToArray();
        if (required.Length == 0)
        {
            throw new ArgumentException("At least one required phase is required.", nameof(requiredPhaseNames));
        }
        if (required.Distinct(StringComparer.OrdinalIgnoreCase).Count() != required.Length)
        {
            throw new ArgumentException("Required phase names must be unique.", nameof(requiredPhaseNames));
        }

        var normalizedEvidence = evidence.Select(Normalize).ToArray();
        if (normalizedEvidence.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalizedEvidence.Length)
        {
            throw new ArgumentException("Phase evidence identities must be unique.", nameof(evidence));
        }

        var byName = normalizedEvidence.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var name in required)
        {
            if (!byName.ContainsKey(name))
            {
                byName[name] = new OrchestrationPhaseEvidence(name, OrchestrationPhaseState.Missing, Required: true);
            }
        }

        var results = byName.Values
            .Select(item => ToResult(item, required.Contains(item.Name, StringComparer.OrdinalIgnoreCase)))
            .OrderByDescending(item => item.Blocker)
            .ThenByDescending(item => item.Required)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();

        var requiredResults = results.Where(item => item.Required).ToArray();
        var completedRequired = requiredResults.Count(item => item.State is OrchestrationPhaseState.Passed or OrchestrationPhaseState.Failed or OrchestrationPhaseState.Skipped);
        var completionRate = (int)Math.Round(100d * completedRequired / requiredResults.Length, MidpointRounding.AwayFromZero);

        var totalWeight = results.Sum(item => item.Weight);
        var passedWeight = results.Where(item => item.State == OrchestrationPhaseState.Passed).Sum(item => item.Weight);
        var qualityScore = totalWeight == 0 ? 0 : (int)Math.Round(100d * passedWeight / totalWeight, MidpointRounding.AwayFromZero);
        qualityScore = Math.Clamp(qualityScore, 0, 100);

        var blockers = results.Count(item => item.Blocker);
        var successful = blockers == 0 && requiredResults.All(item => item.State == OrchestrationPhaseState.Passed);
        var canonical = string.Join('\n', results.Select(item =>
            $"{item.Name}|{item.State}|{item.Required}|{item.StartedAtUtc:O}|{item.CompletedAtUtc:O}|{item.Duration.Ticks}|{item.Weight}|{item.Blocker}"))
            + $"\n{blockers}|{completionRate}|{qualityScore}|{successful}";

        return new OrchestrationQualityResult(results, blockers, completionRate, qualityScore, successful, Hash(canonical));
    }

    public static string NormalizeName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (!NameRegex().IsMatch(normalized))
        {
            throw new ArgumentException("Orchestration phase identity is invalid.", nameof(value));
        }
        return normalized;
    }

    private static OrchestrationPhaseEvidence Normalize(OrchestrationPhaseEvidence item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Weight <= 0 || item.Weight > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(item.Weight), "Phase weight must be between 1 and 100.");
        }

        var started = item.StartedAt?.ToUniversalTime();
        var completed = item.CompletedAt?.ToUniversalTime();
        if (started.HasValue && completed.HasValue && completed.Value < started.Value)
        {
            throw new ArgumentException("Phase completion timestamp cannot precede its start timestamp.", nameof(item));
        }

        return item with { Name = NormalizeName(item.Name), StartedAt = started, CompletedAt = completed };
    }

    private static OrchestrationPhaseResult ToResult(OrchestrationPhaseEvidence item, bool requiredByPlan)
    {
        var required = item.Required || requiredByPlan;
        var duration = item.StartedAt.HasValue && item.CompletedAt.HasValue
            ? item.CompletedAt.Value - item.StartedAt.Value
            : TimeSpan.Zero;
        var blocker = required && item.State is OrchestrationPhaseState.Missing or OrchestrationPhaseState.Pending or OrchestrationPhaseState.Failed or OrchestrationPhaseState.Skipped;
        return new OrchestrationPhaseResult(item.Name, item.State, required, item.StartedAt, item.CompletedAt, duration, item.Weight, blocker);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [GeneratedRegex("^[a-z0-9][a-z0-9_.:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex NameRegex();
}
