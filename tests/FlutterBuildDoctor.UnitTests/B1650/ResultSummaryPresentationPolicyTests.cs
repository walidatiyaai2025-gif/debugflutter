using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B1650;

public sealed class ResultSummaryPresentationPolicyTests
{
    [Fact]
    public void Evaluate_ComputesTotalsPercentageAndStatus()
    {
        var result = ResultSummaryPresentationPolicy.Evaluate("summary", 8, 1, 1);
        Assert.Equal(10, result.TotalCount);
        Assert.Equal(80d, result.SuccessPercent);
        Assert.Equal("failed", result.Status);
        Assert.Equal("result-summary-failed", result.ReasonCode);
    }

    [Fact]
    public void Evaluate_RejectsNegativeCounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ResultSummaryPresentationPolicy.Evaluate("summary", -1, 0, 0));
    }
}
