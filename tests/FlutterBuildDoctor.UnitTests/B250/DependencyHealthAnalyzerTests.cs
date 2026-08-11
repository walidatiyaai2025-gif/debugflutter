using FlutterBuildDoctor.Application.Dependencies;

namespace FlutterBuildDoctor.UnitTests.B250;

public sealed class DependencyHealthAnalyzerTests
{
    [Fact]
    public void NormalizePackage_CanonicalizesAndRejectsUnsupportedCharacters()
    {
        Assert.Equal("shared_preferences", DependencyHealthAnalyzer.NormalizePackage(" Shared-Preferences "));
        Assert.Throws<ArgumentException>(() => DependencyHealthAnalyzer.NormalizePackage("bad/package"));
    }

    [Theory]
    [InlineData("3.24.0-beta.1", true)]
    [InlineData("3.24.0", false)]
    public void IsPrereleaseVersion_DetectsSuffix(string version, bool expected)
    {
        Assert.Equal(expected, DependencyHealthAnalyzer.IsPrereleaseVersion(version));
    }

    [Theory]
    [InlineData("1.2.3", true, false)]
    [InlineData("^1.2.3", false, true)]
    [InlineData(">=1.0.0 <2.0.0", false, true)]
    [InlineData("any", false, true)]
    public void ConstraintClassification_DistinguishesPinnedAndRanges(string constraint, bool pinned, bool range)
    {
        Assert.Equal(pinned, DependencyHealthAnalyzer.IsExactPinnedConstraint(constraint));
        Assert.Equal(range, DependencyHealthAnalyzer.IsRangeOrWildcardConstraint(constraint));
    }

    [Fact]
    public void Analyze_TracksOriginRiskAndDeterministicOrdering()
    {
        var risks = DependencyHealthAnalyzer.Analyze(new[]
        {
            new DependencyEvidence(
                "safe_pkg",
                "1.2.3",
                "1.2.3",
                LatestVersion: "1.9.0"),
            new DependencyEvidence(
                "critical-pkg",
                "1.0.0-beta.1",
                "^1.0.0",
                LatestVersion: "3.0.0",
                Vulnerability: DependencyVulnerabilitySeverity.Critical,
                Deprecated: true,
                Origin: DependencyOrigin.Transitive)
        });

        Assert.Equal(2, risks.Count);
        var critical = risks[0];
        Assert.Equal("critical_pkg", critical.Package);
        Assert.True(critical.IsPrerelease);
        Assert.False(critical.IsExactPinned);
        Assert.True(critical.IsRangeOrWildcard);
        Assert.True(critical.HasMajorDrift);
        Assert.True(critical.Deprecated);
        Assert.Equal(DependencyOrigin.Transitive, critical.Origin);
        Assert.Equal(100, critical.RiskScore);

        Assert.Equal(0, risks[1].RiskScore);
    }

    [Fact]
    public void HasMajorVersionDrift_UsesSuppliedLatestVersionOnly()
    {
        Assert.True(DependencyHealthAnalyzer.HasMajorVersionDrift("1.8.0", "2.0.0"));
        Assert.False(DependencyHealthAnalyzer.HasMajorVersionDrift("2.0.0", "2.9.0"));
        Assert.False(DependencyHealthAnalyzer.HasMajorVersionDrift("2.0.0", null));
    }
}
