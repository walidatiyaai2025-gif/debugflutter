using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B850;

public sealed class NetworkCircuitBreakerPolicyTests
{
    [Fact]
    public void Evaluate_OpensAfterThresholdAndProducesStableFingerprint()
    {
        var now = new DateTimeOffset(2026, 8, 11, 20, 0, 0, TimeSpan.Zero);
        var observations = new[]
        {
            new CircuitObservation(false, now.AddSeconds(-20)),
            new CircuitObservation(false, now.AddSeconds(-10)),
            new CircuitObservation(false, now.AddSeconds(-5))
        };

        var first = NetworkCircuitBreakerPolicy.Evaluate(" API.GITHUB.COM ", observations, 3, TimeSpan.FromMinutes(2), now);
        var second = NetworkCircuitBreakerPolicy.Evaluate("api.github.com", observations, 3, TimeSpan.FromMinutes(2), now);

        Assert.Equal(CircuitState.Open, first.State);
        Assert.Equal(3, first.ConsecutiveFailures);
        Assert.Equal("circuit-open", first.ReasonCode);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void Evaluate_EntersHalfOpenAfterCooldown()
    {
        var now = DateTimeOffset.UtcNow;
        var observations = new[]
        {
            new CircuitObservation(false, now.AddMinutes(-5)),
            new CircuitObservation(false, now.AddMinutes(-4)),
            new CircuitObservation(false, now.AddMinutes(-3))
        };

        var decision = NetworkCircuitBreakerPolicy.Evaluate("mirror", observations, 3, TimeSpan.FromMinutes(2), now);
        Assert.Equal(CircuitState.HalfOpen, decision.State);
        Assert.Equal("circuit-half-open", decision.ReasonCode);
    }

    [Fact]
    public void Evaluate_ResetsOnRecentSuccess()
    {
        var now = DateTimeOffset.UtcNow;
        var observations = new[]
        {
            new CircuitObservation(false, now.AddMinutes(-2)),
            new CircuitObservation(false, now.AddMinutes(-1)),
            new CircuitObservation(true, now.AddSeconds(-10))
        };

        Assert.Equal(CircuitState.Closed,
            NetworkCircuitBreakerPolicy.Evaluate("mirror", observations, 2, TimeSpan.FromMinutes(1), now).State);
    }
}
