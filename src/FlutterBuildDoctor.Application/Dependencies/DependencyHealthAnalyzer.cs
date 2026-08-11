namespace FlutterBuildDoctor.Application.Dependencies;

public enum DependencyVulnerabilitySeverity
{
    None = 0,
    Low = 1,
    Moderate = 2,
    High = 3,
    Critical = 4
}

public enum DependencyOrigin
{
    Direct = 0,
    Transitive = 1
}

public sealed record DependencyEvidence(
    string Package,
    string CurrentVersion,
    string Constraint,
    string? LatestVersion = null,
    DependencyVulnerabilitySeverity Vulnerability = DependencyVulnerabilitySeverity.None,
    bool Deprecated = false,
    DependencyOrigin Origin = DependencyOrigin.Direct);

public sealed record DependencyRisk(
    string Package,
    string CurrentVersion,
    string Constraint,
    bool IsPrerelease,
    bool IsExactPinned,
    bool IsRangeOrWildcard,
    bool HasMajorDrift,
    DependencyVulnerabilitySeverity Vulnerability,
    bool Deprecated,
    DependencyOrigin Origin,
    int RiskScore);

public static class DependencyHealthAnalyzer
{
    public static IReadOnlyList<DependencyRisk> Analyze(IEnumerable<DependencyEvidence> dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        return dependencies
            .Select(AnalyzeOne)
            .OrderByDescending(risk => risk.RiskScore)
            .ThenBy(risk => risk.Package, StringComparer.Ordinal)
            .ToArray();
    }

    public static string NormalizePackage(string package)
    {
        if (string.IsNullOrWhiteSpace(package))
        {
            throw new ArgumentException("Package identity is required.", nameof(package));
        }

        var normalized = package.Trim().ToLowerInvariant().Replace('-', '_');
        if (normalized.Length > 128 || normalized.Any(character => !(char.IsLetterOrDigit(character) || character == '_')))
        {
            throw new ArgumentException("Package identity contains unsupported characters.", nameof(package));
        }

        return normalized;
    }

    public static bool IsPrereleaseVersion(string version)
    {
        var value = RequireVersion(version, nameof(version));
        var dash = value.IndexOf('-', StringComparison.Ordinal);
        return dash > 0 && dash < value.Length - 1;
    }

    public static bool IsExactPinnedConstraint(string constraint)
    {
        var value = RequireConstraint(constraint);
        return value.All(character => char.IsDigit(character) || character == '.') &&
               value.Count(character => character == '.') is 1 or 2;
    }

    public static bool IsRangeOrWildcardConstraint(string constraint)
    {
        var value = RequireConstraint(constraint);
        return value.IndexOfAny(new[] { '^', '~', '>', '<', '*', 'x', 'X', '|' }) >= 0 ||
               value.Any(char.IsWhiteSpace) ||
               !IsExactPinnedConstraint(value);
    }

    public static bool HasMajorVersionDrift(string currentVersion, string? latestVersion)
    {
        if (string.IsNullOrWhiteSpace(latestVersion)) return false;
        var currentMajor = ParseMajor(currentVersion);
        var latestMajor = ParseMajor(latestVersion);
        return latestMajor > currentMajor;
    }

    private static DependencyRisk AnalyzeOne(DependencyEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var package = NormalizePackage(evidence.Package);
        var current = RequireVersion(evidence.CurrentVersion, nameof(evidence.CurrentVersion));
        var constraint = RequireConstraint(evidence.Constraint);
        var prerelease = IsPrereleaseVersion(current);
        var exact = IsExactPinnedConstraint(constraint);
        var range = IsRangeOrWildcardConstraint(constraint);
        var drift = HasMajorVersionDrift(current, evidence.LatestVersion);

        var score = VulnerabilityScore(evidence.Vulnerability);
        if (evidence.Deprecated) score += 20;
        if (drift) score += 20;
        if (prerelease) score += 10;
        if (range) score += 5;
        if (evidence.Origin == DependencyOrigin.Transitive) score += 5;

        return new DependencyRisk(
            package,
            current,
            constraint,
            prerelease,
            exact,
            range,
            drift,
            evidence.Vulnerability,
            evidence.Deprecated,
            evidence.Origin,
            Math.Clamp(score, 0, 100));
    }

    private static int VulnerabilityScore(DependencyVulnerabilitySeverity severity) => severity switch
    {
        DependencyVulnerabilitySeverity.None => 0,
        DependencyVulnerabilitySeverity.Low => 10,
        DependencyVulnerabilitySeverity.Moderate => 25,
        DependencyVulnerabilitySeverity.High => 40,
        DependencyVulnerabilitySeverity.Critical => 60,
        _ => throw new ArgumentOutOfRangeException(nameof(severity))
    };

    private static int ParseMajor(string version)
    {
        var value = RequireVersion(version, nameof(version));
        var start = 0;
        while (start < value.Length && !char.IsDigit(value[start])) start++;
        var end = start;
        while (end < value.Length && char.IsDigit(value[end])) end++;
        if (start == end || !int.TryParse(value[start..end], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var major))
        {
            throw new ArgumentException("Version does not contain a major number.", nameof(version));
        }

        return major;
    }

    private static string RequireVersion(string? version, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(version) || version.Length > 128 || version.Any(char.IsControl))
        {
            throw new ArgumentException("Dependency version is invalid.", parameterName);
        }

        return version.Trim();
    }

    private static string RequireConstraint(string? constraint)
    {
        if (string.IsNullOrWhiteSpace(constraint) || constraint.Length > 256 || constraint.Any(char.IsControl))
        {
            throw new ArgumentException("Dependency constraint is invalid.", nameof(constraint));
        }

        return constraint.Trim();
    }
}
