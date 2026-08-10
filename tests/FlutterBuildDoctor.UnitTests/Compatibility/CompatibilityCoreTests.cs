using FlutterBuildDoctor.Application.Compatibility;

namespace FlutterBuildDoctor.UnitTests.Compatibility;

public sealed class CompatibilityCoreTests
{
    [Theory]
    [InlineData("8.11.1", "8.11.1", 0)]
    [InlineData("8.11.2", "8.11.1", 1)]
    [InlineData("2.4.10-RC1", "2.4.10", -1)]
    [InlineData("17", "8.0", 1)]
    public void SemanticVersion_OrdersRepresentativeToolVersions(string left, string right, int expectedSign)
    {
        var comparison = SemanticVersion.Parse(left).CompareTo(SemanticVersion.Parse(right));

        Assert.Equal(expectedSign, Math.Sign(comparison));
    }

    [Theory]
    [InlineData(">=3.0.0 <4.0.0", "3.9.0", true)]
    [InlineData(">=3.0.0 <4.0.0", "4.0.0", false)]
    [InlineData("^3.2.0", "3.9.9", true)]
    [InlineData("^3.2.0", "4.0.0", false)]
    [InlineData("^0.2.3", "0.2.9", true)]
    [InlineData("^0.2.3", "0.3.0", false)]
    public void VersionConstraint_EvaluatesPubStyleRanges(string expression, string version, bool expected)
    {
        var constraint = VersionConstraint.Parse(expression);

        Assert.Equal(expected, constraint.Contains(SemanticVersion.Parse(version)));
    }

    [Theory]
    [InlineData("21", "8.4", true)]
    [InlineData("21", "8.5", false)]
    [InlineData("17", "7.2", true)]
    [InlineData("17", "7.3", false)]
    [InlineData("11", "9.0", true)]
    public void JavaGradleRule_FlagsKnownRuntimeIncompatibilities(string java, string gradle, bool blocked)
    {
        var findings = new JavaGradleCompatibilityRule().Evaluate(new CompatibilityContext(JavaVersion: java, GradleVersion: gradle));

        Assert.Equal(blocked, findings.Any(finding => finding.Severity == CompatibilitySeverity.Blocker));
    }

    [Theory]
    [InlineData("8.9.2", "8.10", true)]
    [InlineData("8.9.2", "8.11.1", false)]
    [InlineData("9.3.0", "9.4.1", true)]
    [InlineData("9.3.0", "9.5.0", false)]
    public void GradleAgpRule_UsesCapturedMinimumGradleVersions(string agp, string gradle, bool blocked)
    {
        var findings = new GradleAgpCompatibilityRule().Evaluate(new CompatibilityContext(
            GradleVersion: gradle,
            AndroidGradlePluginVersion: agp));

        Assert.Equal(blocked, findings.Any(finding => finding.Severity == CompatibilitySeverity.Blocker));
    }

    [Theory]
    [InlineData(36, "8.8.2", true)]
    [InlineData(36, "8.9.1", false)]
    [InlineData(35, "8.5.2", true)]
    [InlineData(35, "8.6.0", false)]
    [InlineData(37, "9.1.0", true)]
    [InlineData(37, "9.1.1", false)]
    public void AgpCompileSdkRule_EnforcesAndroidMinimumToolTable(int compileSdk, string agp, bool blocked)
    {
        var findings = new AgpCompileSdkCompatibilityRule().Evaluate(new CompatibilityContext(
            AndroidGradlePluginVersion: agp,
            CompileSdk: compileSdk));

        Assert.Equal(blocked, findings.Any(finding => finding.Severity == CompatibilitySeverity.Blocker));
    }

    [Fact]
    public void KotlinRule_FlagsGradleAndAgpOutsideSupportedBand()
    {
        var findings = new KotlinGradleAgpCompatibilityRule().Evaluate(new CompatibilityContext(
            GradleVersion: "9.6.0",
            AndroidGradlePluginVersion: "9.2.0",
            KotlinVersion: "2.4.10"));

        Assert.Equal(2, findings.Count(finding => finding.Severity == CompatibilitySeverity.Blocker));
    }

    [Theory]
    [InlineData("3.9.0", ">=3.0.0 <4.0.0", false)]
    [InlineData("4.0.0", ">=3.0.0 <4.0.0", true)]
    [InlineData("3.5.0", "^3.2.0", false)]
    public void DartConstraintRule_ValidatesProjectConstraint(string dart, string constraint, bool blocked)
    {
        var findings = new DartConstraintCompatibilityRule().Evaluate(new CompatibilityContext(
            DartVersion: dart,
            DartSdkConstraint: constraint));

        Assert.Equal(blocked, findings.Any(finding => finding.Severity == CompatibilitySeverity.Blocker));
    }

    [Fact]
    public void AndroidPackageRule_ReportsMissingPlatformAndBuildTools()
    {
        var findings = new AndroidPackageAvailabilityRule().Evaluate(new CompatibilityContext(
            CompileSdk: 36,
            InstalledAndroidPlatforms: new[] { 34, 35 },
            RequiredBuildToolsVersion: "36.0.0",
            InstalledBuildToolsVersions: new[] { "35.0.0" }));

        Assert.Equal(2, findings.Count);
        Assert.All(findings, finding => Assert.Equal(CompatibilitySeverity.Blocker, finding.Severity));
    }

    [Fact]
    public void Engine_AggregatesSeverityAndReadinessWithoutHidingBlockers()
    {
        var engine = CompatibilityEngine.CreateDefault();

        var report = engine.Evaluate(new CompatibilityContext(
            JavaVersion: "21",
            GradleVersion: "8.4",
            AndroidGradlePluginVersion: "8.8.0",
            KotlinVersion: "2.4.10",
            DartVersion: "4.0.0",
            DartSdkConstraint: ">=3.0.0 <4.0.0",
            CompileSdk: 36,
            InstalledAndroidPlatforms: new[] { 35 },
            RequiredBuildToolsVersion: "36.0.0",
            InstalledBuildToolsVersions: new[] { "35.0.0" }));

        Assert.True(report.IsBlocked);
        Assert.True(report.BlockerCount >= 4);
        Assert.Equal(0, report.ReadinessScore);
        Assert.All(report.Findings.Take(report.BlockerCount), finding => Assert.Equal(CompatibilitySeverity.Blocker, finding.Severity));
    }
}
