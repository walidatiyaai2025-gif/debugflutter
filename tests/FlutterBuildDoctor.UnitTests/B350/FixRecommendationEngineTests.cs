using FlutterBuildDoctor.Application.Recommendations;

namespace FlutterBuildDoctor.UnitTests.B350;

public sealed class FixRecommendationEngineTests
{
    [Fact]
    public void Rank_NormalizesClampsAndPrefersSafeFixes()
    {
        var result = FixRecommendationEngine.Rank(new[]
        {
            new FixRecommendation("danger", " delete   build cache ", 120, FixRisk.Destructive, " GRADLE "),
            new FixRecommendation("safe", "Run flutter clean", 40, FixRisk.Safe, " FLUTTER "),
            new FixRecommendation("risky", "Update wrapper", -10, FixRisk.Risky)
        });

        Assert.Equal(new[] { "safe", "risky", "danger" }, result.Recommendations.Select(item => item.Id));
        Assert.Equal(100, result.Recommendations.Single(item => item.Id == "danger").Confidence);
        Assert.Equal(0, result.Recommendations.Single(item => item.Id == "risky").Confidence);
        Assert.Equal("delete build cache", result.Recommendations.Single(item => item.Id == "danger").Title);
        Assert.Equal("gradle", result.Recommendations.Single(item => item.Id == "danger").SourceProblemCode);
        Assert.True(result.Recommendations.Single(item => item.Id == "danger").RequiresConfirmation);
        Assert.False(result.Recommendations.Single(item => item.Id == "safe").RequiresConfirmation);
    }

    [Fact]
    public void Rank_DeduplicatesEquivalentIdsUsingSafestHighestConfidenceCandidate()
    {
        var result = FixRecommendationEngine.Rank(new[]
        {
            new FixRecommendation("fix-1", "Danger", 99, FixRisk.Destructive),
            new FixRecommendation("FIX-1", "Safe", 60, FixRisk.Safe),
            new FixRecommendation("fix-1", "Safer", 80, FixRisk.Safe)
        });

        var recommendation = Assert.Single(result.Recommendations);
        Assert.Equal(FixRisk.Safe, recommendation.Risk);
        Assert.Equal(80, recommendation.Confidence);
        Assert.Equal("Safer", recommendation.Title);
    }

    [Fact]
    public void Rank_BoundsCountAndAssignsStableRanks()
    {
        var source = Enumerable.Range(0, 20)
            .Select(index => new FixRecommendation($"fix-{index:D2}", $"Fix {index}", 50, FixRisk.Safe));

        var result = FixRecommendationEngine.Rank(source, maxRecommendations: 3);

        Assert.Equal(3, result.Recommendations.Count);
        Assert.Equal(new[] { 1, 2, 3 }, result.Recommendations.Select(item => item.Rank));
    }

    [Fact]
    public void Rank_IsDeterministicAcrossInputOrder()
    {
        var source = new[]
        {
            new FixRecommendation("b", "B", 70, FixRisk.Safe, "p2"),
            new FixRecommendation("a", "A", 70, FixRisk.Safe, "p1")
        };

        var first = FixRecommendationEngine.Rank(source);
        var second = FixRecommendationEngine.Rank(source.AsEnumerable().Reverse());

        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(first.Recommendations.Select(item => item.Id), second.Recommendations.Select(item => item.Id));
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Fact]
    public void Rank_RejectsInvalidRecommendationIdentity()
    {
        Assert.Throws<ArgumentException>(() => FixRecommendationEngine.Rank(new[]
        {
            new FixRecommendation("bad id", "Fix", 50, FixRisk.Safe)
        }));
    }
}
