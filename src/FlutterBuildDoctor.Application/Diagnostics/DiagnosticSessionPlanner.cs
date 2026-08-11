using System.Security.Cryptography;
using System.Text;

namespace FlutterBuildDoctor.Application.Diagnostics;

public enum DiagnosticStepPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

public sealed record DiagnosticStep(
    string Id,
    int Weight = 1,
    DiagnosticStepPriority Priority = DiagnosticStepPriority.Normal,
    bool IsBlocker = false);

public sealed record DiagnosticStepState(string Id, bool Completed, bool Failed = false);

public sealed record DiagnosticSessionPlan(
    Guid SessionId,
    DateTimeOffset StartedAtUtc,
    IReadOnlyList<DiagnosticStep> Steps,
    string Fingerprint);

public sealed record DiagnosticSessionProgress(
    int Percent,
    bool ShouldStop,
    bool IsCancelled,
    bool HasBlockerFailure);

public static class DiagnosticSessionPlanner
{
    public const int MaxSteps = 64;

    public static DiagnosticSessionPlan Create(
        IEnumerable<DiagnosticStep> steps,
        DateTimeOffset startedAt,
        Guid? sessionId = null)
    {
        ArgumentNullException.ThrowIfNull(steps);

        var materialized = steps.ToArray();
        if (materialized.Length is < 1 or > MaxSteps)
        {
            throw new ArgumentOutOfRangeException(nameof(steps), $"A diagnostic session requires 1..{MaxSteps} steps.");
        }

        foreach (var step in materialized)
        {
            ValidateId(step.Id, nameof(steps));
            if (step.Weight is < 1 or > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(steps), "Diagnostic step weight must be between 1 and 100.");
            }
        }

        if (materialized.Select(step => step.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != materialized.Length)
        {
            throw new ArgumentException("Diagnostic step IDs must be unique.", nameof(steps));
        }

        var ordered = materialized
            .OrderByDescending(step => step.Priority)
            .ThenBy(step => step.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var resolvedSessionId = sessionId ?? Guid.NewGuid();
        if (resolvedSessionId == Guid.Empty)
        {
            throw new ArgumentException("Diagnostic session ID cannot be empty.", nameof(sessionId));
        }

        var startedAtUtc = startedAt.ToUniversalTime();
        return new DiagnosticSessionPlan(
            resolvedSessionId,
            startedAtUtc,
            ordered,
            Fingerprint(ordered, startedAtUtc));
    }

    public static DiagnosticSessionProgress EvaluateProgress(
        DiagnosticSessionPlan plan,
        IEnumerable<DiagnosticStepState> states,
        bool isCancelled = false)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(states);

        var stateMap = states
            .GroupBy(state => state.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

        var totalWeight = plan.Steps.Sum(step => step.Weight);
        var completedWeight = plan.Steps
            .Where(step => stateMap.TryGetValue(step.Id, out var state) && state.Completed)
            .Sum(step => step.Weight);
        var percent = totalWeight == 0
            ? 0
            : (int)Math.Clamp(Math.Round(completedWeight * 100d / totalWeight, MidpointRounding.AwayFromZero), 0, 100);

        var blockerFailure = plan.Steps.Any(step =>
            step.IsBlocker &&
            stateMap.TryGetValue(step.Id, out var state) &&
            state.Failed);

        return new DiagnosticSessionProgress(
            percent,
            ShouldStop: blockerFailure || isCancelled,
            IsCancelled: isCancelled,
            HasBlockerFailure: blockerFailure);
    }

    public static bool IsEvidenceStale(
        DateTimeOffset capturedAt,
        DateTimeOffset now,
        TimeSpan maxAge)
    {
        if (maxAge <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAge));
        }

        return now.ToUniversalTime() - capturedAt.ToUniversalTime() > maxAge;
    }

    private static void ValidateId(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(char.IsControl))
        {
            throw new ArgumentException("Diagnostic step ID is invalid.", parameterName);
        }
    }

    private static string Fingerprint(IReadOnlyList<DiagnosticStep> steps, DateTimeOffset startedAtUtc)
    {
        var canonical = new StringBuilder();
        canonical.Append(startedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        foreach (var step in steps)
        {
            canonical
                .Append('|')
                .Append(step.Id.Trim().ToUpperInvariant())
                .Append(':')
                .Append(step.Weight)
                .Append(':')
                .Append((int)step.Priority)
                .Append(':')
                .Append(step.IsBlocker ? '1' : '0');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }
}
