using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B650;

public sealed class RecoveryActionPlanningPolicyTests
{
    [Fact]
    public void Plan_OrdersActionsAndRequiresCheckpointBeforeDestructiveWork()
    {
        var actions = new[]
        {
            new RecoveryActionCandidate("repair", true, false, false, true, 2),
            new RecoveryActionCandidate("checkpoint", false, true, true, false, 1)
        };

        var result = RecoveryActionPlanningPolicy.Plan(actions);

        Assert.Equal(new[] { "checkpoint", "repair" }, result.Actions.Select(item => item.Id));
        Assert.Equal(1, result.ReversibleCount);
        Assert.Equal(1, result.DestructiveCount);
        Assert.Equal(45, result.RiskScore);
        Assert.Equal("recovery-plan-confirmed", result.ReasonCode);
    }

    [Fact]
    public void Plan_RejectsDestructiveActionWithoutPriorCheckpoint()
        => Assert.Throws<ArgumentException>(() => RecoveryActionPlanningPolicy.Plan(new[]
        {
            new RecoveryActionCandidate("repair", true, false, false, true, 1)
        }));

    [Fact]
    public void Plan_RejectsUnconfirmedDestructiveAction()
        => Assert.Throws<ArgumentException>(() => RecoveryActionPlanningPolicy.Plan(new[]
        {
            new RecoveryActionCandidate("checkpoint", false, true, true, false, 1),
            new RecoveryActionCandidate("repair", true, false, false, false, 2)
        }));

    [Fact]
    public void Plan_IsDeterministicAcrossInputOrder()
    {
        var actions = new[]
        {
            new RecoveryActionCandidate("verify", false, true, false, false, 2),
            new RecoveryActionCandidate("checkpoint", false, true, true, false, 1)
        };
        var first = RecoveryActionPlanningPolicy.Plan(actions);
        var second = RecoveryActionPlanningPolicy.Plan(actions.AsEnumerable().Reverse());
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal("recovery-plan-safe", first.ReasonCode);
    }
}
