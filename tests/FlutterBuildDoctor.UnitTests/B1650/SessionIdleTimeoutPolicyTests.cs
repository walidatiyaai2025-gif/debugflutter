using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B1650;

public sealed class SessionIdleTimeoutPolicyTests
{
    [Fact]
    public void Evaluate_ClassifiesActiveAndExpiredSessions()
    {
        var now = DateTimeOffset.Parse("2026-08-13T12:00:00Z");
        var active = SessionIdleTimeoutPolicy.Evaluate("session", now.AddMinutes(-5), now, TimeSpan.FromMinutes(30));
        Assert.True(active.Active);
        Assert.False(active.ExpirationRequired);

        var expired = SessionIdleTimeoutPolicy.Evaluate("session", now.AddHours(-2), now, TimeSpan.FromMinutes(30));
        Assert.False(expired.Active);
        Assert.True(expired.ExpirationRequired);
        Assert.Equal("session-idle-expired", expired.ReasonCode);
    }

    [Fact]
    public void Evaluate_RejectsActivityBeyondFutureTolerance()
    {
        var now = DateTimeOffset.Parse("2026-08-13T12:00:00Z");
        Assert.Throws<ArgumentException>(() => SessionIdleTimeoutPolicy.Evaluate("session", now.AddMinutes(10), now, TimeSpan.FromMinutes(30)));
    }
}
