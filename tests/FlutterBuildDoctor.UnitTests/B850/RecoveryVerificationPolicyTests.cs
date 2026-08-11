using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B850;

public sealed class RecoveryVerificationPolicyTests
{
    [Fact]
    public void Evaluate_CompletesWhenMandatoryChecksPass()
    {
        var checks = new[]
        {
            new RecoveryVerificationCheck(" build ", true, true, 5),
            new RecoveryVerificationCheck("tests", true, true, 3),
            new RecoveryVerificationCheck("telemetry", false, false, 1)
        };

        var first = RecoveryVerificationPolicy.Evaluate(" REPAIR-1 ", checks);
        var second = RecoveryVerificationPolicy.Evaluate("repair-1", checks.OrderByDescending(item => item.Name));

        Assert.True(first.Complete);
        Assert.Empty(first.FailedMandatoryChecks);
        Assert.InRange(first.Score, 0, 100);
        Assert.Equal("recovery-verified", first.ReasonCode);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void Evaluate_FailsWhenMandatoryCheckFails()
    {
        var decision = RecoveryVerificationPolicy.Evaluate("repair", new[]
        {
            new RecoveryVerificationCheck("build", true, true, 1),
            new RecoveryVerificationCheck("tests", true, false, 1)
        });

        Assert.False(decision.Complete);
        Assert.Equal(new[] { "tests" }, decision.FailedMandatoryChecks);
        Assert.Equal(50, decision.Score);
        Assert.Equal("recovery-verification-failed", decision.ReasonCode);
    }

    [Fact]
    public void Evaluate_RejectsDuplicateCheckNames()
        => Assert.Throws<ArgumentException>(() => RecoveryVerificationPolicy.Evaluate("repair", new[]
        {
            new RecoveryVerificationCheck("build", true, true),
            new RecoveryVerificationCheck("BUILD", false, true)
        }));
}
