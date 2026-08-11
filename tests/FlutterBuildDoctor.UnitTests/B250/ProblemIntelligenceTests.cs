using FlutterBuildDoctor.Application.Intelligence;

namespace FlutterBuildDoctor.UnitTests.B250;

public sealed class ProblemIntelligenceTests
{
    [Fact]
    public void NormalizeSignature_CanonicalizesAndRejectsInvalidValues()
    {
        Assert.Equal("gradle-build-failure", ProblemIntelligence.NormalizeSignature("  Gradle   Build Failure "));
        Assert.Throws<ArgumentException>(() => ProblemIntelligence.NormalizeSignature("   "));
    }

    [Theory]
    [InlineData("fatal blocker cannot continue", ProblemSeverity.Blocker)]
    [InlineData("Gradle build failed with exception", ProblemSeverity.Error)]
    [InlineData("warning: package deprecated", ProblemSeverity.Warning)]
    [InlineData("all checks passed", ProblemSeverity.Info)]
    public void ClassifySeverity_MapsEvidence(string message, ProblemSeverity expected)
    {
        Assert.Equal(expected, ProblemIntelligence.ClassifySeverity(message));
    }

    [Fact]
    public void InferComponent_UsesExplicitValueThenEvidenceKeywords()
    {
        Assert.Equal("custom", ProblemIntelligence.InferComponent("flutter failed", " Custom "));
        Assert.Equal("flutter", ProblemIntelligence.InferComponent("Flutter doctor failed"));
        Assert.Equal("gradle", ProblemIntelligence.InferComponent("Gradle task failed"));
        Assert.Equal("general", ProblemIntelligence.InferComponent("Unknown problem"));
    }

    [Fact]
    public void Analyze_DeduplicatesCountsDatesConfidenceAndRanksActions()
    {
        var first = new DateTimeOffset(2026, 8, 10, 20, 0, 0, TimeSpan.FromHours(3));
        var last = first.AddHours(2);
        var clusters = ProblemIntelligence.Analyze(new[]
        {
            new ProblemEvidence(
                " Gradle Build ",
                "Gradle build failed",
                last,
                Confidence: 140,
                Actions: new[] { new SuggestedAction("clean", 1), new SuggestedAction("upgrade", 3) }),
            new ProblemEvidence(
                "gradle   build",
                "Gradle warning detected",
                first,
                Confidence: -10,
                Actions: new[] { new SuggestedAction("clean", 5) }),
            new ProblemEvidence("info", "Everything is fine", first, Actionable: false)
        });

        Assert.Equal(2, clusters.Count);
        var gradle = clusters[0];
        Assert.Equal("gradle-build", gradle.Signature);
        Assert.Equal(ProblemSeverity.Error, gradle.Severity);
        Assert.Equal("gradle", gradle.Component);
        Assert.Equal(2, gradle.Occurrences);
        Assert.Equal(first.ToUniversalTime(), gradle.FirstSeenUtc);
        Assert.Equal(last.ToUniversalTime(), gradle.LastSeenUtc);
        Assert.Equal(50, gradle.Confidence);
        Assert.True(gradle.Actionable);
        Assert.Equal(new[] { "clean", "upgrade" }, gradle.SuggestedActions.Select(action => action.Id));
        Assert.Equal(new[] { 5, 3 }, gradle.SuggestedActions.Select(action => action.Priority));
        Assert.False(clusters[1].Actionable);
    }
}
