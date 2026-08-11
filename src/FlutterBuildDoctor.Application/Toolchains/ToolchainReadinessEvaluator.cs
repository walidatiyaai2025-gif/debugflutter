using System.Security.Cryptography;
using System.Text;

namespace FlutterBuildDoctor.Application.Toolchains;

public sealed record ToolRequirement(string Name, string MinimumVersion, bool Required = true);

public sealed record ToolEvidence(
    string Name,
    bool IsAvailable,
    string? Version = null,
    string? ExecutablePath = null,
    DateTimeOffset? DiscoveredAt = null);

public sealed record ToolReadinessItem(
    string Name,
    bool Required,
    bool IsReady,
    string MinimumVersion,
    string? DiscoveredVersion,
    string? ExecutablePath,
    string ReasonCode);

public sealed record ToolchainReadinessDecision(
    IReadOnlyList<ToolReadinessItem> Items,
    IReadOnlyList<string> Blockers,
    int ReadinessScore,
    DateTimeOffset EvaluatedAtUtc,
    string Fingerprint);

public static class ToolchainReadinessEvaluator
{
    public const int MaxTools = 64;

    public static ToolchainReadinessDecision Evaluate(
        IEnumerable<ToolRequirement> requirements,
        IEnumerable<ToolEvidence> evidence,
        DateTimeOffset evaluatedAt)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(evidence);

        var normalizedRequirements = requirements
            .Select(NormalizeRequirement)
            .ToArray();

        if (normalizedRequirements.Length == 0 || normalizedRequirements.Length > MaxTools)
        {
            throw new ArgumentOutOfRangeException(nameof(requirements), $"Tool requirements must contain 1..{MaxTools} entries.");
        }

        if (normalizedRequirements.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalizedRequirements.Length)
        {
            throw new ArgumentException("Tool requirements contain duplicate identities.", nameof(requirements));
        }

        var evidenceMap = evidence
            .Select(NormalizeEvidence)
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.DiscoveredAt ?? DateTimeOffset.MinValue).First(),
                StringComparer.OrdinalIgnoreCase);

        var items = normalizedRequirements
            .Select(requirement => EvaluateItem(requirement, evidenceMap.GetValueOrDefault(requirement.Name)))
            .OrderByDescending(item => item.Required)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();

        var blockers = items
            .Where(item => item.Required && !item.IsReady)
            .Select(item => $"{item.Name}:{item.ReasonCode}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var readyCount = items.Count(item => item.IsReady);
        var score = Math.Clamp((int)Math.Round(readyCount * 100d / items.Length, MidpointRounding.AwayFromZero), 0, 100);
        var evaluatedAtUtc = evaluatedAt.ToUniversalTime();
        var fingerprint = ComputeFingerprint(items, evaluatedAtUtc);

        return new ToolchainReadinessDecision(items, blockers, score, evaluatedAtUtc, fingerprint);
    }

    public static string NormalizeVersion(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        var value = version.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        var suffixIndex = value.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0)
        {
            value = value[..suffixIndex];
        }

        if (!Version.TryParse(value, out var parsed))
        {
            throw new ArgumentException("Version evidence is not a valid semantic version.", nameof(version));
        }

        return parsed.ToString();
    }

    private static ToolRequirement NormalizeRequirement(ToolRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        var name = NormalizeName(requirement.Name);
        var minimumVersion = NormalizeVersion(requirement.MinimumVersion);
        return requirement with { Name = name, MinimumVersion = minimumVersion };
    }

    private static ToolEvidence NormalizeEvidence(ToolEvidence item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var name = NormalizeName(item.Name);
        var version = string.IsNullOrWhiteSpace(item.Version) ? null : NormalizeVersion(item.Version);
        var executablePath = string.IsNullOrWhiteSpace(item.ExecutablePath) ? null : item.ExecutablePath.Trim();
        var discoveredAt = item.DiscoveredAt?.ToUniversalTime();
        return item with { Name = name, Version = version, ExecutablePath = executablePath, DiscoveredAt = discoveredAt };
    }

    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalized = name.Trim().ToLowerInvariant();
        if (normalized.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Tool identity cannot contain whitespace.", nameof(name));
        }

        return normalized;
    }

    private static ToolReadinessItem EvaluateItem(ToolRequirement requirement, ToolEvidence? evidence)
    {
        if (evidence is null || !evidence.IsAvailable)
        {
            return new ToolReadinessItem(
                requirement.Name,
                requirement.Required,
                false,
                requirement.MinimumVersion,
                evidence?.Version,
                evidence?.ExecutablePath,
                "missing");
        }

        if (evidence.Version is null)
        {
            return new ToolReadinessItem(
                requirement.Name,
                requirement.Required,
                false,
                requirement.MinimumVersion,
                null,
                evidence.ExecutablePath,
                "version-missing");
        }

        var actual = Version.Parse(evidence.Version);
        var minimum = Version.Parse(requirement.MinimumVersion);
        var ready = actual >= minimum;
        return new ToolReadinessItem(
            requirement.Name,
            requirement.Required,
            ready,
            requirement.MinimumVersion,
            evidence.Version,
            evidence.ExecutablePath,
            ready ? "ready" : "below-minimum");
    }

    private static string ComputeFingerprint(IEnumerable<ToolReadinessItem> items, DateTimeOffset evaluatedAtUtc)
    {
        var canonicalItems = items.Select(item => string.Join(':',
            item.Name,
            item.Required ? "required" : "optional",
            item.IsReady ? "ready" : "blocked",
            item.MinimumVersion,
            item.DiscoveredVersion ?? string.Empty,
            item.ExecutablePath ?? string.Empty,
            item.ReasonCode));
        var canonical = string.Join('|', canonicalItems.Append(evaluatedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
