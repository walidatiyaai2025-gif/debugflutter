using System.Security.Cryptography;
using System.Text;

namespace FlutterBuildDoctor.Application.Reliability;

public enum ReliabilityGateState
{
    Missing = 0,
    Passed = 1,
    Failed = 2,
    Skipped = 3
}

public enum ReliabilityGateSeverity
{
    Info = 0,
    Warning = 1,
    Blocker = 2
}

public sealed record ReliabilityGateEvidence(
    string Name,
    ReliabilityGateState State,
    bool Required,
    int Weight = 1,
    ReliabilityGateSeverity DeclaredSeverity = ReliabilityGateSeverity.Info);

public sealed record EvaluatedReliabilityGate(
    string Name,
    ReliabilityGateState State,
    bool Required,
    int Weight,
    ReliabilityGateSeverity Severity);

public sealed record ReliabilityGateDecision(
    IReadOnlyList<EvaluatedReliabilityGate> Gates,
    int RequiredPassRate,
    int ReadinessScore,
    int BlockerCount,
    bool ReleaseEligible,
    string Fingerprint);

public static class ReliabilityGateEvaluator
{
    public static ReliabilityGateDecision Evaluate(
        IEnumerable<string> requiredGateNames,
        IEnumerable<ReliabilityGateEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(requiredGateNames);
        ArgumentNullException.ThrowIfNull(evidence);

        var required = requiredGateNames.Select(ValidateName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (required.Length == 0)
        {
            throw new ArgumentException("At least one required reliability gate must be declared.", nameof(requiredGateNames));
        }

        var materialized = evidence.Select(NormalizeEvidence).ToArray();
        if (materialized.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != materialized.Length)
        {
            throw new ArgumentException("Reliability gate evidence contains duplicate names.", nameof(evidence));
        }

        var byName = materialized.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        var evaluated = new List<EvaluatedReliabilityGate>(materialized.Length + required.Length);

        foreach (var item in materialized)
        {
            var isRequired = item.Required || required.Contains(item.Name, StringComparer.OrdinalIgnoreCase);
            evaluated.Add(new EvaluatedReliabilityGate(
                item.Name,
                item.State,
                isRequired,
                item.Weight,
                NormalizeSeverity(item.State, isRequired, item.DeclaredSeverity)));
        }

        foreach (var requiredName in required)
        {
            if (byName.ContainsKey(requiredName)) continue;
            evaluated.Add(new EvaluatedReliabilityGate(
                requiredName,
                ReliabilityGateState.Missing,
                Required: true,
                Weight: 1,
                ReliabilityGateSeverity.Blocker));
        }

        var ordered = evaluated
            .OrderByDescending(item => item.Severity)
            .ThenByDescending(item => item.Required)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var requiredGates = ordered.Where(item => item.Required).ToArray();
        var passedRequired = requiredGates.Count(item => item.State == ReliabilityGateState.Passed);
        var passRate = requiredGates.Length == 0
            ? 100
            : (int)Math.Round(passedRequired * 100d / requiredGates.Length, MidpointRounding.AwayFromZero);

        var weightedTotal = ordered.Sum(item => item.Weight);
        var weightedPassed = ordered.Where(item => item.State == ReliabilityGateState.Passed).Sum(item => item.Weight);
        var readiness = weightedTotal == 0
            ? 0
            : (int)Math.Round(weightedPassed * 100d / weightedTotal, MidpointRounding.AwayFromZero);
        readiness = Math.Clamp(readiness, 0, 100);

        var blockers = ordered.Count(item => item.Severity == ReliabilityGateSeverity.Blocker);
        var releaseEligible = blockers == 0;
        var fingerprint = Fingerprint(ordered, passRate, readiness, blockers, releaseEligible);

        return new ReliabilityGateDecision(ordered, passRate, readiness, blockers, releaseEligible, fingerprint);
    }

    public static ReliabilityGateSeverity NormalizeSeverity(
        ReliabilityGateState state,
        bool required,
        ReliabilityGateSeverity declaredSeverity)
    {
        if (!Enum.IsDefined(state)) throw new ArgumentOutOfRangeException(nameof(state));
        if (!Enum.IsDefined(declaredSeverity)) throw new ArgumentOutOfRangeException(nameof(declaredSeverity));

        if (required && state is ReliabilityGateState.Missing or ReliabilityGateState.Failed or ReliabilityGateState.Skipped)
        {
            return ReliabilityGateSeverity.Blocker;
        }

        if (state == ReliabilityGateState.Failed)
        {
            return declaredSeverity == ReliabilityGateSeverity.Blocker
                ? ReliabilityGateSeverity.Blocker
                : ReliabilityGateSeverity.Warning;
        }

        if (state == ReliabilityGateState.Skipped)
        {
            return ReliabilityGateSeverity.Warning;
        }

        return declaredSeverity;
    }

    private static ReliabilityGateEvidence NormalizeEvidence(ReliabilityGateEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var name = ValidateName(evidence.Name);
        if (evidence.Weight is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(evidence), "Reliability gate weight must be 1..100.");
        }

        if (!Enum.IsDefined(evidence.State)) throw new ArgumentOutOfRangeException(nameof(evidence.State));
        if (!Enum.IsDefined(evidence.DeclaredSeverity)) throw new ArgumentOutOfRangeException(nameof(evidence.DeclaredSeverity));
        return evidence with { Name = name };
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Reliability gate name is required.", nameof(name));
        }

        var normalized = string.Join('-', name.Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length > 96 || normalized.Any(char.IsControl))
        {
            throw new ArgumentException("Reliability gate name is invalid.", nameof(name));
        }

        return normalized;
    }

    private static string Fingerprint(
        IEnumerable<EvaluatedReliabilityGate> gates,
        int passRate,
        int readiness,
        int blockers,
        bool releaseEligible)
    {
        var canonical = string.Join("\n", gates.Select(item =>
            $"{item.Name}|{(int)item.State}|{item.Required}|{item.Weight}|{(int)item.Severity}"));
        canonical += $"\n{passRate}|{readiness}|{blockers}|{releaseEligible}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
