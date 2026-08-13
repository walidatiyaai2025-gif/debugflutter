using System;
using FlutterBuildDoctor.Application.Governance;
using Xunit;

namespace FlutterBuildDoctor.UnitTests.B1550;

public sealed class ExecutionPhaseTransitionPolicyTests
{
    [Fact]
    public void Evaluate_NormalizesAndAllowsOrderedTransitions()
    {
        var normal = ExecutionPhaseTransitionPolicy.Evaluate(" Session-A ", "RUNNING", "verifying", 2, 3);
        var failure = ExecutionPhaseTransitionPolicy.Evaluate("session-a", "preparing", "failed", 1, 2);
        Assert.Equal("session-a", normal.SessionIdentity);
        Assert.Equal("running", normal.CurrentPhase);
        Assert.True(normal.Allowed);
        Assert.False(normal.Terminal);
        Assert.True(failure.Allowed);
        Assert.True(failure.Terminal);
        Assert.Equal("phase-transition-allowed", failure.ReasonCode);
        Assert.Equal(64, normal.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_BlocksRegressionSkipAndTerminalExit()
    {
        Assert.False(ExecutionPhaseTransitionPolicy.Evaluate("s", "queued", "running", 1, 2).Allowed);
        Assert.Equal("phase-transition-sequence-regression", ExecutionPhaseTransitionPolicy.Evaluate("s", "queued", "preparing", 2, 2).ReasonCode);
        Assert.Equal("phase-transition-terminal", ExecutionPhaseTransitionPolicy.Evaluate("s", "completed", "failed", 3, 4).ReasonCode);
        Assert.Throws<ArgumentOutOfRangeException>(() => ExecutionPhaseTransitionPolicy.Evaluate("s", "queued", "preparing", -1, 0));
        Assert.Throws<ArgumentException>(() => ExecutionPhaseTransitionPolicy.Evaluate("s", "unknown", "running", 0, 1));
    }
}
