using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B1650;

public sealed class WorkspaceIsolationPolicyTests
{
    [Fact]
    public void Evaluate_DetectsOutsideChildAndWorkspaceOverlap()
    {
        var result = WorkspaceIsolationBoundaryPolicy.Evaluate(
            new[]
            {
                new WorkspaceBoundary("one", "work/root"),
                new WorkspaceBoundary("two", "work/root/nested")
            },
            new[] { ("one", "other/file.txt") });

        Assert.False(result.Isolated);
        Assert.Contains(result.Findings, item => item.Kind == "child-outside-root");
        Assert.Contains(result.Findings, item => item.Kind == "workspace-overlap");
        Assert.Equal("workspace-isolation-violation", result.ReasonCode);
    }

    [Fact]
    public void Evaluate_IsDeterministicForValidBoundaries()
    {
        var result = WorkspaceIsolationBoundaryPolicy.Evaluate(
            new[] { new WorkspaceBoundary("one", "work/root") },
            new[] { ("one", "work/root/file.txt") });

        Assert.True(result.Isolated);
        Assert.Equal(64, result.Fingerprint.Length);
    }
}
