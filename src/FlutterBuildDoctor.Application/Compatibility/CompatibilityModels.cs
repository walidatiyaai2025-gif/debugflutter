namespace FlutterBuildDoctor.Application.Compatibility;

public enum CompatibilitySeverity
{
    Info = 0,
    Warning,
    Blocker
}

public sealed record CompatibilitySource(
    string Name,
    string DocumentationUrl,
    DateOnly SnapshotDate);

public sealed record CompatibilityFinding(
    string RuleId,
    CompatibilitySeverity Severity,
    string Category,
    string Message,
    string? Current = null,
    string? Required = null,
    string? Recommended = null,
    CompatibilitySource? Source = null)
{
    public int RiskWeight => Severity switch
    {
        CompatibilitySeverity.Blocker => 100,
        CompatibilitySeverity.Warning => 15,
        _ => 0
    };
}

public sealed record CompatibilityContext(
    string? JavaVersion = null,
    string? GradleVersion = null,
    string? AndroidGradlePluginVersion = null,
    string? KotlinVersion = null,
    string? DartVersion = null,
    string? DartSdkConstraint = null,
    int? CompileSdk = null,
    IReadOnlyCollection<int>? InstalledAndroidPlatforms = null,
    string? RequiredBuildToolsVersion = null,
    IReadOnlyCollection<string>? InstalledBuildToolsVersions = null);

public sealed record CompatibilityReport(IReadOnlyList<CompatibilityFinding> Findings)
{
    public int BlockerCount => Findings.Count(static finding => finding.Severity == CompatibilitySeverity.Blocker);
    public int WarningCount => Findings.Count(static finding => finding.Severity == CompatibilitySeverity.Warning);
    public int InfoCount => Findings.Count(static finding => finding.Severity == CompatibilitySeverity.Info);
    public bool IsBlocked => BlockerCount > 0;
    public int RiskScore => Math.Min(100, Findings.Sum(static finding => finding.RiskWeight));
    public int ReadinessScore => 100 - RiskScore;
}

public interface ICompatibilityRule
{
    string RuleId { get; }
    IReadOnlyList<CompatibilityFinding> Evaluate(CompatibilityContext context);
}

public interface ICompatibilityEngine
{
    CompatibilityReport Evaluate(CompatibilityContext context);
}
