using System;
using FlutterBuildDoctor.Application.Governance;
using Xunit;

namespace FlutterBuildDoctor.UnitTests.B1550;

public sealed class CancellationPropagationPolicyTests
{
    [Fact]
    public void Evaluate_PropagatesAndPreservesShieldedSubtree()
    {
        var nodes = new[]
        {
            new CancellationOperationNode("root", null, false),
            new CancellationOperationNode("child-a", "root", false),
            new CancellationOperationNode("child-b", "root", true),
            new CancellationOperationNode("grand-a", "child-a", false),
            new CancellationOperationNode("grand-b", "child-b", false)
        };
        var decision = CancellationPropagationPolicy.Evaluate(nodes, "ROOT");
        Assert.Equal(new[] { "child-a", "grand-a", "root" }, decision.CancelledOperationIds);
        Assert.Equal(new[] { "child-b" }, decision.ShieldedOperationIds);
        Assert.DoesNotContain("grand-b", decision.CancelledOperationIds);
        Assert.Equal("cancellation-propagated-with-shields", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_RejectsBrokenTrees()
    {
        Assert.Throws<ArgumentException>(() => CancellationPropagationPolicy.Evaluate(new[] { new CancellationOperationNode("a", null, false), new CancellationOperationNode("a", null, false) }, "a"));
        Assert.Throws<ArgumentException>(() => CancellationPropagationPolicy.Evaluate(new[] { new CancellationOperationNode("a", "missing", false) }, "a"));
        Assert.Throws<ArgumentException>(() => CancellationPropagationPolicy.Evaluate(new[] { new CancellationOperationNode("a", "a", false) }, "a"));
        Assert.Throws<ArgumentException>(() => CancellationPropagationPolicy.Evaluate(new[] { new CancellationOperationNode("a", "b", false), new CancellationOperationNode("b", "a", false) }, "a"));
    }
}
