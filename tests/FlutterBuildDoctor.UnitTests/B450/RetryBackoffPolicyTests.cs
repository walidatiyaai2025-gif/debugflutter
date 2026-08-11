using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.UnitTests.B450;

public sealed class RetryBackoffPolicyTests
{
    [Fact]
    public void Build_ClampsAndProducesExponentialSchedule()
    {
        var first = RetryBackoffPolicy.Build(99, TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(2), RetryFailureKind.Transient);
        var second = RetryBackoffPolicy.Build(99, TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(2), RetryFailureKind.Transient);

        Assert.True(first.RetryAllowed);
        Assert.Equal(RetryBackoffPolicy.MaxRetryCount, first.RetryCount);
        Assert.Equal(RetryBackoffPolicy.MinBaseDelay, first.BaseDelay);
        Assert.Equal(new[]
        {
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromMilliseconds(400),
            TimeSpan.FromMilliseconds(800),
            TimeSpan.FromMilliseconds(1600)
        }, first.Schedule);
        Assert.Equal("transient-retry", first.ReasonCode);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Theory]
    [InlineData(RetryFailureKind.Cancelled, "cancelled-no-retry")]
    [InlineData(RetryFailureKind.Permanent, "permanent-no-retry")]
    public void Build_DisablesRetryForNonTransientFailures(RetryFailureKind kind, string reason)
    {
        var decision = RetryBackoffPolicy.Build(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), kind);
        Assert.False(decision.RetryAllowed);
        Assert.Empty(decision.Schedule);
        Assert.Equal(reason, decision.ReasonCode);
    }

    [Fact]
    public void Build_ZeroBudgetDisablesTransientRetry()
    {
        var decision = RetryBackoffPolicy.Build(-1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), RetryFailureKind.Transient);
        Assert.Equal(0, decision.RetryCount);
        Assert.False(decision.RetryAllowed);
        Assert.Equal("retry-budget-zero", decision.ReasonCode);
    }

    [Fact]
    public void Build_CapsMaximumDelay()
    {
        var decision = RetryBackoffPolicy.Build(5, TimeSpan.FromSeconds(30), TimeSpan.FromHours(1), RetryFailureKind.Transient);
        Assert.Equal(RetryBackoffPolicy.MaxSupportedDelay, decision.MaxDelay);
        Assert.All(decision.Schedule, delay => Assert.True(delay <= RetryBackoffPolicy.MaxSupportedDelay));
    }
}
