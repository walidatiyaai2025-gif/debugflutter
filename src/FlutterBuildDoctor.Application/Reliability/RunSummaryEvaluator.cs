using System.Security.Cryptography;
using System.Text;

namespace FlutterBuildDoctor.Application.Reliability;

public enum RunPhaseState
{
    Passed = 0,
    Failed = 1,
    Skipped = 2,
    Missing = 3
}

public sealed record RunPhaseEvidence(
    string Name,
    RunPhaseState State,
    bool Required = true,
    int Weight = 1,
    DateTimeOffset? CompletedAt = null);

public sealed record RunSummaryDecision(
    IReadOnlyList<RunPhaseEvidence> Phases,
    int BlockerCount,
    int RequiredPassRate,
    int QualityScore,
    bool Successful,
    DateTimeOffset CompletedAtUtc,
    string Fingerprint);

public static class RunSummaryEvaluator
{
    public const int MaxPhases = 64;
    public const int MaxWeight = 100;

    public static RunSummaryDecision Evaluate(
        IEnumerable<string> requiredPhaseNames,
        IEnumerable<RunPhaseEvidence> evidence,
        DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(requiredPhaseNames);
        ArgumentNullException.ThrowIfNull(evidence);

        var required = requiredPhaseNames.Select(NormalizeName).ToArray();
        if (required.Length == 0 || required.Length > MaxPhases)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredPhaseNames), $"Required phases must contain 1..{MaxPhases} entries.");
        }

        if (required.Distinct(StringComparer.OrdinalIgnoreCase).Count() != required.Length)
        {
            throw new ArgumentException("Required phase names contain duplicates.", nameof(requiredPhaseNames));
        }

        var normalizedEvidence = evidence.Select(NormalizeEvidence).ToArray();
        if (normalizedEvidence.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalizedEvidence.Length)
        {
            throw new ArgumentException("Phase evidence contains duplicate names.", nameof(evidence));
        }

        var evidenceMap = normalizedEvidence.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        var phases = normalizedEvidence.ToList();
        foreach (var requiredName in required)
        {
            if (!evidenceMap.ContainsKey(requiredName))
            {
                phases.Add(new RunPhaseEvidence(requiredName, RunPhaseState.Missing, Required: true));
            }
        }

        phases = phases
            .OrderByDescending(IsBlocker)
            .ThenByDescending(item => item.Required)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToList();

        var requiredPhases = phases.Where(item => required.Contains(item.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
        var passedRequired = requiredPhases.Count(item => item.State == RunPhaseState.Passed);
        var requiredPassRate = Math.Clamp((int)Math.Round(passedRequired * 100d / requiredPhases.Length, MidpointRounding.AwayFromZero), 0, 100);

        var totalWeight = phases.Sum(item => item.Weight);
        var passedWeight = phases.Where(item => item.State == RunPhaseState.Passed).Sum(item => item.Weight);
        var qualityScore = totalWeight == 0
            ? 0
            : Math.Clamp((int)Math.Round(passedWeight * 100d / totalWeight, MidpointRounding.AwayFromZero), 0, 100);

        var blockerCount = phases.Count(IsBlocker);
        var successful = blockerCount == 0;
        var completedAtUtc = completedAt.ToUniversalTime();
        var fingerprint = ComputeFingerprint(phases, blockerCount, requiredPassRate, qualityScore, successful, completedAtUtc);

        return new RunSummaryDecision(phases, blockerCount, requiredPassRate, qualityScore, successful, completedAtUtc, fingerprint);
    }

    public static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalized = name.Trim().ToLowerInvariant();
        if (normalized.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Run phase name cannot contain whitespace.", nameof(name));
        }

        return normalized;
    }

    private static RunPhaseEvidence NormalizeEvidence(RunPhaseEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var name = NormalizeName(evidence.Name);
        var weight = Math.Clamp(evidence.Weight, 1, MaxWeight);
        var completedAt = evidence.CompletedAt?.ToUniversalTime();
        return evidence with { Name = name, Weight = weight, CompletedAt = completedAt };
    }

    private static bool IsBlocker(RunPhaseEvidence evidence) => evidence.Required
        && evidence.State is RunPhaseState.Failed or RunPhaseState.Missing or RunPhaseState.Skipped;

    private static string ComputeFingerprint(
        IEnumerable<RunPhaseEvidence> phases,
        int blockerCount,
        int requiredPassRate,
        int qualityScore,
        bool successful,
        DateTimeOffset completedAtUtc)
    {
        var canonicalPhases = phases.Select(item => string.Join(':',
            item.Name,
            item.State,
            item.Required,
            item.Weight,
            item.CompletedAt?.ToString("O", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty));
        var header = string.Join(':',
            blockerCount,
            requiredPassRate,
            qualityScore,
            successful,
            completedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        var canonical = string.Join('|', canonicalPhases.Prepend(header));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
