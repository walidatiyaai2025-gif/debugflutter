using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B650;

public sealed class ExecutionTimeoutBudgetPolicyTests
{
    [Fact]
    public void Evaluate_ClampsBudgetsAndComputesRemainingTime()
    {
        var phases = new[]
        {
            new PhaseTimeoutBudget(" Build ", TimeSpan.FromSeconds(1)),
            new PhaseTimeoutBudget("test", TimeSpan.FromMinutes(10))
        };

        var result = ExecutionTimeoutBudgetPolicy.Evaluate(
            TimeSpan.FromHours(10), phases, TimeSpan.FromMinutes(30),
            TimeSpan.FromSeconds(1), TimeSpan.Zero);

        Assert.Equal(ExecutionTimeoutBudgetPolicy.MaxTotalBudget, result.TotalBudget);
        Assert.Equal(ExecutionTimeoutBudgetPolicy.MinCleanupReserve, result.CleanupReserve);
        Assert.Equal(ExecutionTimeoutBudgetPolicy.MinCancellationGrace, result.CancellationGrace);
        Assert.Equal("build", result.Phases[0].Phase);
        Assert.Equal(ExecutionTimeoutBudgetPolicy.MinPhaseBudget, result.Phases[0].Timeout);
        Assert.Equal(TimeSpan.FromMinutes(90), result.Remaining);
        Assert.False(result.Exhausted);
        Assert.Equal("timeout-budget-ready", result.ReasonCode);
    }

    [Fact]
    public void Evaluate_DetectsExhaustedBudget()
    {
        var result = ExecutionTimeoutBudgetPolicy.Evaluate(
            TimeSpan.FromMinutes(5), Array.Empty<PhaseTimeoutBudget>(), TimeSpan.FromMinutes(6),
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));
        Assert.True(result.Exhausted);
        Assert.Equal(TimeSpan.Zero, result.Remaining);
        Assert.Equal("timeout-budget-exhausted", result.ReasonCode);
    }

    [Fact]
    public void Evaluate_RejectsPhaseAllocationBeyondAvailableBudget()
        => Assert.Throws<ArgumentException>(() => ExecutionTimeoutBudgetPolicy.Evaluate(
            TimeSpan.FromMinutes(2),
            new[] { new PhaseTimeoutBudget("build", TimeSpan.FromMinutes(2)) },
            TimeSpan.Zero, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5)));

    [Fact]
    public void Evaluate_IsDeterministicAcrossPhaseInputOrder()
    {
        var phases = new[]
        {
            new PhaseTimeoutBudget("test", TimeSpan.FromMinutes(1)),
            new PhaseTimeoutBudget("build", TimeSpan.FromMinutes(1))
        };
        var first = ExecutionTimeoutBudgetPolicy.Evaluate(TimeSpan.FromMinutes(5), phases, TimeSpan.Zero, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));
        var second = ExecutionTimeoutBudgetPolicy.Evaluate(TimeSpan.FromMinutes(5), phases.AsEnumerable().Reverse(), TimeSpan.Zero, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }
}
