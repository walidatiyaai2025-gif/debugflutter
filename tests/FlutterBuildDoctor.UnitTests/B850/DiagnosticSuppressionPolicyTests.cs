using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B850;

public sealed class DiagnosticSuppressionPolicyTests
{
    [Fact]
    public void Evaluate_AppliesExactNonExpiredSuppression()
    {
        var now = new DateTimeOffset(2026, 8, 11, 20, 0, 0, TimeSpan.Zero);
        var rule = new DiagnosticSuppressionRule("gradle-timeout", "repo-a", now.AddHours(-1), now.AddHours(1), false);

        var decision = DiagnosticSuppressionPolicy.Evaluate(rule, " GRADLE-TIMEOUT ", "REPO-A", "warning", now);

        Assert.True(decision.Suppressed);
        Assert.False(decision.Expired);
        Assert.Equal("diagnostic-suppressed", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_DeniesPermanentBlockerSuppression()
    {
        var now = DateTimeOffset.UtcNow;
        var rule = new DiagnosticSuppressionRule("signing-failure", "repo", now, null, true);
        var decision = DiagnosticSuppressionPolicy.Evaluate(rule, "signing-failure", "repo", "blocker", now);

        Assert.False(decision.Suppressed);
        Assert.Equal("blocker-suppression-denied", decision.ReasonCode);
    }

    [Fact]
    public void Evaluate_ClampsLongExpiryAndDetectsExpiredRule()
    {
        var created = DateTimeOffset.UtcNow.AddDays(-40);
        var rule = new DiagnosticSuppressionRule("noise", "repo", created, created.AddDays(90), false);
        var decision = DiagnosticSuppressionPolicy.Evaluate(rule, "noise", "repo", "info", DateTimeOffset.UtcNow);

        Assert.True(decision.Expired);
        Assert.False(decision.Suppressed);
        Assert.Equal(created.ToUniversalTime() + DiagnosticSuppressionPolicy.MaxLifetime, decision.EffectiveExpiryUtc);
    }
}
