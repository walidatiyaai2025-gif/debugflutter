using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B1650;

public sealed class StatusSummaryAggregationPolicyTests
{
    [Fact]
    public void Evaluate_CountsStatusesAndBlocksRequiredErrors()
    {
        var result = StatusSummaryAggregationPolicy.Evaluate(new[]
        {
            new StatusSummaryItem("restore", "ready", true),
            new StatusSummaryItem("build", "warning", true),
            new StatusSummaryItem("test", "error", true),
            new StatusSummaryItem("optional", "error", false)
        });

        Assert.Equal(1, result.ReadyCount);
        Assert.Equal(1, result.WarningCount);
        Assert.Equal(2, result.ErrorCount);
        Assert.False(result.Ready);
        Assert.Equal(new[] { "test" }, result.BlockingIds);
    }

    [Fact]
    public void Evaluate_RejectsUnsupportedStatus()
    {
        Assert.Throws<ArgumentException>(() => StatusSummaryAggregationPolicy.Evaluate(new[] { new StatusSummaryItem("item", "unknown", true) }));
    }
}
