using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B750;

public sealed class PathOwnershipPolicyTests
{
    [Fact]
    public void Evaluate_AllowsNestedPathAndProducesStableRelativeEvidence()
    {
        var root = Path.Combine(Path.GetTempPath(), "fbd-b750-owner");
        var candidate = Path.Combine(root, "src", "app");

        var first = PathOwnershipPolicy.Evaluate(" Workspace ", root, candidate);
        var second = PathOwnershipPolicy.Evaluate("workspace", root, candidate);

        Assert.True(first.Allowed);
        Assert.Equal("workspace", first.Scope);
        Assert.Equal("src/app", first.RelativePath);
        Assert.Equal(2, first.Depth);
        Assert.Equal("path-owned", first.ReasonCode);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void Evaluate_RejectsCandidateOutsideOwnerRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "fbd-b750-owner-a");
        var outside = Path.Combine(Path.GetTempPath(), "fbd-b750-owner-b", "file.txt");

        var result = PathOwnershipPolicy.Evaluate("workspace", root, outside);

        Assert.False(result.Allowed);
        Assert.Equal("path-outside-owner-root", result.ReasonCode);
    }

    [Fact]
    public void Evaluate_RejectsRootMutationByDefaultButCanDescribeRootWhenAllowed()
    {
        var root = Path.Combine(Path.GetTempPath(), "fbd-b750-root");

        var blocked = PathOwnershipPolicy.Evaluate("workspace", root, root);
        var allowed = PathOwnershipPolicy.Evaluate("workspace", root, root, forbidRootMutation: false);

        Assert.False(blocked.Allowed);
        Assert.Equal("owner-root-mutation-forbidden", blocked.ReasonCode);
        Assert.True(allowed.Allowed);
        Assert.Equal(".", allowed.RelativePath);
    }

    [Fact]
    public void Evaluate_RejectsRelativeRoot()
        => Assert.Throws<ArgumentException>(() => PathOwnershipPolicy.Evaluate("workspace", "relative/root", "relative/root/file"));
}
