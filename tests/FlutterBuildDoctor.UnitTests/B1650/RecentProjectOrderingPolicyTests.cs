using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B1650;

public sealed class RecentProjectOrderingPolicyTests
{
    [Fact]
    public void Evaluate_PrioritizesPinnedThenRecencyAndClampsLimit()
    {
        var now = DateTimeOffset.Parse("2026-08-13T12:00:00Z");
        var result = RecentProjectOrderingPolicy.Evaluate(new[]
        {
            new RecentProjectItem("old-pinned", now.AddDays(-2), true),
            new RecentProjectItem("new", now, false),
            new RecentProjectItem("recent-pinned", now.AddDays(-1), true)
        }, 2);

        Assert.Equal(new[] { "recent-pinned", "old-pinned" }, result.Selected.Select(item => item.Identity));
        Assert.Equal(2, result.PinnedCount);
        Assert.Equal("recent-projects-trimmed", result.ReasonCode);
    }

    [Fact]
    public void Evaluate_RejectsDuplicateProjects()
    {
        Assert.Throws<ArgumentException>(() => RecentProjectOrderingPolicy.Evaluate(new[]
        {
            new RecentProjectItem("project", DateTimeOffset.UtcNow, false),
            new RecentProjectItem("PROJECT", DateTimeOffset.UtcNow, false)
        }, 10));
    }
}
