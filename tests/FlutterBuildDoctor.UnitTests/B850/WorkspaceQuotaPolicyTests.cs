using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B850;

public sealed class WorkspaceQuotaPolicyTests
{
    [Fact]
    public void Evaluate_ClampsQuotaAndComputesCapacity()
    {
        var decision = WorkspaceQuotaPolicy.Evaluate(" Repo-A ", 32L * 1024 * 1024, 1, 8L * 1024 * 1024);

        Assert.Equal("repo-a", decision.WorkspaceIdentity);
        Assert.Equal(WorkspaceQuotaPolicy.MinQuotaBytes, decision.QuotaBytes);
        Assert.False(decision.Exhausted);
        Assert.True(decision.RemainingBytes > 0);
        Assert.InRange(decision.UsagePercent, 0, 100);
        Assert.Equal("workspace-quota-available", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_ExhaustsAtEffectiveQuota()
    {
        const long quota = 100L * 1024 * 1024;
        const long reserve = 20L * 1024 * 1024;
        var decision = WorkspaceQuotaPolicy.Evaluate("repo", 90L * 1024 * 1024, quota, reserve);

        Assert.True(decision.Exhausted);
        Assert.Equal(0, decision.RemainingBytes);
        Assert.Equal(100, decision.UsagePercent);
        Assert.Equal("workspace-quota-exhausted", decision.ReasonCode);
    }

    [Fact]
    public void Evaluate_RejectsNegativeUsage()
        => Assert.Throws<ArgumentOutOfRangeException>(() => WorkspaceQuotaPolicy.Evaluate("repo", -1, 100, 0));
}
