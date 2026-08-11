using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record OperationalReadinessCheck(
    string Identity,
    string Category,
    bool Mandatory,
    bool Passed,
    int Weight = 1);

public sealed record OperationalReadinessDecision(
    IReadOnlyList<OperationalReadinessCheck> Checks,
    IReadOnlyList<string> MandatoryBlockers,
    int Score,
    bool Ready,
    string ReasonCode,
    string Fingerprint);

public static class OperationalReadinessGate
{
    public const int DefaultMaxChecks = 128;
    private static readonly Regex IdentityPattern = new("^[a-z0-9][a-z0-9._:-]{0,127}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static OperationalReadinessDecision Evaluate(
        IEnumerable<OperationalReadinessCheck> checks,
        IEnumerable<string>? requiredMandatoryIdentities = null,
        int maxChecks = DefaultMaxChecks)
    {
        ArgumentNullException.ThrowIfNull(checks);
        maxChecks = Math.Clamp(maxChecks, 1, 1024);
        var normalized = checks.Select(Normalize).ToArray();
        if (normalized.Length > maxChecks)
        {
            throw new ArgumentOutOfRangeException(nameof(checks), $"Readiness check count exceeds {maxChecks}.");
        }

        var byId = new Dictionary<string, OperationalReadinessCheck>(StringComparer.OrdinalIgnoreCase);
        foreach (var check in normalized)
        {
            if (!byId.TryAdd(check.Identity, check))
            {
                throw new ArgumentException($"Duplicate readiness check '{check.Identity}'.", nameof(checks));
            }
        }

        var required = (requiredMandatoryIdentities ?? Array.Empty<string>())
            .Select(value => NormalizeIdentity(value, "required readiness identity"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var blockers = new List<string>();
        foreach (var check in normalized.Where(item => item.Mandatory && !item.Passed))
        {
            blockers.Add("failed:" + check.Identity);
        }

        foreach (var requiredIdentity in required)
        {
            if (!byId.TryGetValue(requiredIdentity, out var check) || !check.Mandatory)
            {
                blockers.Add("missing-mandatory:" + requiredIdentity);
            }
        }

        var orderedChecks = normalized.OrderBy(check => check.Category, StringComparer.Ordinal)
            .ThenBy(check => check.Identity, StringComparer.Ordinal)
            .ToArray();
        var orderedBlockers = blockers.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var totalWeight = orderedChecks.Sum(check => check.Weight);
        var passedWeight = orderedChecks.Where(check => check.Passed).Sum(check => check.Weight);
        var score = totalWeight == 0 ? 100 : Math.Clamp((int)Math.Round(passedWeight * 100d / totalWeight, MidpointRounding.AwayFromZero), 0, 100);
        var ready = orderedBlockers.Length == 0;
        var reason = ready ? "operational-readiness-ready" : "operational-readiness-blocked";
        var canonicalChecks = string.Join("\n", orderedChecks.Select(check =>
            $"{check.Identity}|{check.Category}|{check.Mandatory}|{check.Passed}|{check.Weight}"));
        var canonical = canonicalChecks + "\nblockers=" + string.Join(',', orderedBlockers) + $"\nscore={score}|ready={ready}|reason={reason}";

        return new OperationalReadinessDecision(
            orderedChecks,
            orderedBlockers,
            score,
            ready,
            reason,
            Hash(canonical));
    }

    private static OperationalReadinessCheck Normalize(OperationalReadinessCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        return new OperationalReadinessCheck(
            NormalizeIdentity(check.Identity, "readiness check identity"),
            NormalizeIdentity(check.Category, "readiness category"),
            check.Mandatory,
            check.Passed,
            Math.Clamp(check.Weight, 1, 100));
    }

    private static string NormalizeIdentity(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{label} is required.", nameof(value));
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (!IdentityPattern.IsMatch(normalized))
        {
            throw new ArgumentException($"Unsafe {label} '{value}'.", nameof(value));
        }

        return normalized;
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
