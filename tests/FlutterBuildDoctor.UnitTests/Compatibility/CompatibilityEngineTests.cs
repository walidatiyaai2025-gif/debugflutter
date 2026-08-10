using FlutterBuildDoctor.Domain.Compatibility;

namespace FlutterBuildDoctor.UnitTests.Compatibility;

public sealed class CompatibilityEngineTests
{
    [Theory]
    [InlineData("3.24.5", 3, 24, 5, null)]
    [InlineData("v17", 17, 0, 0, null)]
    [InlineData("8.7.0-rc.1+build.4", 8, 7, 0, "rc.1")]
    public void SemanticVersion_ParsesSupportedForms(string text, int major, int minor, int patch, string? preRelease)
    {
        Assert.True(SemanticVersion.TryParse(text, out var parsed));
        Assert.Equal(new SemanticVersion(major, minor, patch, preRelease), parsed);
    }

    [Fact]
    public void SemanticVersion_OrdersPrereleaseBeforeStable()
    {
        Assert.True(SemanticVersion.Parse("8.7.0-rc.1") < SemanticVersion.Parse("8.7.0"));
        Assert.True(SemanticVersion.Parse("8.7.0-rc.2") > SemanticVersion.Parse("8.7.0-rc.1"));
    }

    [Theory]
    [InlineData(">=17", "17.0.0", true)]
    [InlineData(">=17", "16.0.2", false)]
    [InlineData("8.5.0..8.9.9", "8.7.2", true)]
    [InlineData("8.5.0..8.9.9", "9.0.0", false)]
    [InlineData("=3.6.0", "3.6.0", true)]
    [InlineData("3.6.0", "3.6.1", false)]
    public void VersionConstraint_ParsesAndEvaluates(string expression, string version, bool expected)
    {
        var constraint = VersionConstraint.Parse(expression);
        Assert.Equal(expected, constraint.IsSatisfiedBy(SemanticVersion.Parse(version)));
    }

    [Fact]
    public void Evaluate_ReturnsReadyMatrix_WhenAllRequirementsAreSatisfied()
    {
        var engine = new CompatibilityEngine();
        var matrix = engine.Evaluate(
            new CompatibilitySnapshot(
                SemanticVersion.Parse("17"),
                SemanticVersion.Parse("8.7.3"),
                SemanticVersion.Parse("8.6.1"),
                SemanticVersion.Parse("2.0.21"),
                SemanticVersion.Parse("3.35.2"),
                SemanticVersion.Parse("3.9.0"),
                35,
                new[] { 34, 35 },
                new[] { "34.0.0", "35.0.0" }),
            CreateRequirements());

        Assert.True(matrix.IsReady);
        Assert.Equal(0, matrix.BlockerCount);
        Assert.Equal(7, matrix.ReadyCount);
        Assert.Equal(100, matrix.Score);
        Assert.All(matrix.Findings, finding => Assert.False(string.IsNullOrWhiteSpace(finding.Evidence)));
    }

    [Fact]
    public void Evaluate_ReportsBlockers_WithCurrentRequiredRecommendedAndEvidence()
    {
        var engine = new CompatibilityEngine();
        var matrix = engine.Evaluate(
            new CompatibilitySnapshot(
                SemanticVersion.Parse("11"),
                SemanticVersion.Parse("7.6"),
                SemanticVersion.Parse("7.4"),
                SemanticVersion.Parse("1.7.10"),
                SemanticVersion.Parse("3.35.2"),
                SemanticVersion.Parse("3.6.0"),
                33,
                new[] { 33 },
                new[] { "33.0.2" }),
            CreateRequirements());

        Assert.False(matrix.IsReady);
        Assert.True(matrix.BlockerCount >= 6);
        Assert.True(matrix.Score < 100);
        Assert.Contains(matrix.Findings, f => f.Area == CompatibilityArea.JavaGradle && f.IsBlocker && f.Current == "11.0.0");
        Assert.Contains(matrix.Findings, f => f.Area == CompatibilityArea.AgpCompileSdk && f.IsBlocker && f.Message.Contains("compileSdk 33", StringComparison.Ordinal));
        Assert.Contains(matrix.Findings, f => f.Area == CompatibilityArea.AndroidPackages && f.IsBlocker && f.Evidence.Contains("35", StringComparison.Ordinal));
        Assert.All(matrix.Findings, finding =>
        {
            Assert.False(string.IsNullOrWhiteSpace(finding.Current));
            Assert.False(string.IsNullOrWhiteSpace(finding.Required));
            Assert.False(string.IsNullOrWhiteSpace(finding.Recommended));
            Assert.False(string.IsNullOrWhiteSpace(finding.Evidence));
        });
    }

    [Fact]
    public void Evaluate_TreatsMissingDetectedVersionsAsBlockers_NotExceptions()
    {
        var engine = new CompatibilityEngine();
        var matrix = engine.Evaluate(
            new CompatibilitySnapshot(null, null, null, null, null, null, null, Array.Empty<int>(), Array.Empty<string>()),
            CreateRequirements());

        Assert.False(matrix.IsReady);
        Assert.True(matrix.BlockerCount > 0);
        Assert.Contains(matrix.Findings, finding => finding.Current == "Not detected");
        Assert.Equal(0, matrix.Score);
    }

    private static CompatibilityRequirements CreateRequirements()
        => new(
            VersionConstraint.Parse(">=17"),
            VersionConstraint.Parse("8.5.0..8.9.9"),
            VersionConstraint.Parse(">=8.0.0"),
            VersionConstraint.Parse(">=2.0.0"),
            VersionConstraint.Parse(">=3.8.0"),
            35,
            new[] { 35 },
            new[] { "35.0.0" },
            RecommendedJava: "17",
            RecommendedGradle: "8.7.3",
            RecommendedAgp: "8.6.1",
            RecommendedKotlin: "2.0.21",
            RecommendedDart: "3.9.0");
}
