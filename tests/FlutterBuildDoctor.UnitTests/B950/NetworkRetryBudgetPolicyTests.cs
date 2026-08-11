using System;
using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B950;

public sealed class NetworkRetryBudgetPolicyTests
{
    [Fact]
    public void Evaluate_ClampsBudgetAndAppliesDeterministicBackoff()
    {
        var decision = NetworkRetryBudgetPolicy.Evaluate(
            " HTTPS://EXAMPLE.COM/api/ ",
            attemptNumber: 3,
            maxAttempts: 99,
            baseDelay: TimeSpan.FromMilliseconds(1),
            maxDelay: TimeSpan.FromHours(1),
            elapsed: TimeSpan.FromSeconds(2));

        Assert.Equal(10, decision.MaxAttempts);
        Assert.Equal(NetworkRetryBudgetPolicy.MinBaseDelay, decision.BaseDelay);
        Assert.Equal(NetworkRetryBudgetPolicy.AbsoluteMaxDelay, decision.MaxDelay);
        Assert.Equal(TimeSpan.FromMilliseconds(400), decision.NextDelay);
        Assert.False(decision.Exhausted);
        Assert.Equal("network-retry-budget-available", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_RespectsRetryAfterAndCapsItAtMaximumDelay()
    {
        var decision = NetworkRetryBudgetPolicy.Evaluate(
            "endpoint", 1, 4,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.Zero,
            TimeSpan.FromSeconds(20));

        Assert.Equal(TimeSpan.FromSeconds(5), decision.NextDelay);
    }

    [Fact]
    public void Evaluate_MarksBudgetExhaustedAtMaximumAttempts()
    {
        var decision = NetworkRetryBudgetPolicy.Evaluate(
            "endpoint", 4, 4,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(3));

        Assert.True(decision.Exhausted);
        Assert.Equal(TimeSpan.Zero, decision.NextDelay);
        Assert.Equal("network-retry-budget-exhausted", decision.ReasonCode);
    }

    [Fact]
    public void Evaluate_RejectsNegativeElapsedAndAttemptValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NetworkRetryBudgetPolicy.Evaluate(
            "endpoint", -1, 4, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => NetworkRetryBudgetPolicy.Evaluate(
            "endpoint", 1, 4, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(-1)));
    }
}
