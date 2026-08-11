using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B850;

public sealed class OperationLeasePolicyTests
{
    [Fact]
    public void Acquire_ClampsDurationAndNormalizesLease()
    {
        var now = new DateTimeOffset(2026, 8, 11, 20, 0, 0, TimeSpan.Zero);
        var decision = OperationLeasePolicy.Acquire(" BUILD-LEASE ", " AGENT-A ", now, TimeSpan.FromHours(2), now);

        Assert.True(decision.Acquired);
        Assert.Equal("build-lease", decision.Lease.Identity);
        Assert.Equal("agent-a", decision.Lease.Owner);
        Assert.Equal(OperationLeasePolicy.MaxDuration, decision.Lease.Duration);
        Assert.Equal("lease-acquired", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Acquire_AllowsSameOwnerRenewal()
    {
        var now = DateTimeOffset.UtcNow;
        var existing = new OperationLease("build", "agent-a", now.AddMinutes(-1), TimeSpan.FromMinutes(5));
        var decision = OperationLeasePolicy.Acquire("build", "AGENT-A", now, TimeSpan.FromMinutes(10), now, existing);

        Assert.True(decision.Acquired);
        Assert.False(decision.ExpiredExistingLease);
        Assert.Equal("lease-renewed", decision.ReasonCode);
    }

    [Fact]
    public void Acquire_RejectsConflictingActiveOwner()
    {
        var now = DateTimeOffset.UtcNow;
        var existing = new OperationLease("build", "agent-a", now.AddMinutes(-1), TimeSpan.FromMinutes(5));
        var decision = OperationLeasePolicy.Acquire("build", "agent-b", now, TimeSpan.FromMinutes(5), now, existing);

        Assert.False(decision.Acquired);
        Assert.Equal("lease-conflict", decision.ReasonCode);
    }

    [Fact]
    public void Acquire_ReacquiresExpiredLease()
    {
        var now = DateTimeOffset.UtcNow;
        var existing = new OperationLease("build", "agent-a", now.AddMinutes(-10), TimeSpan.FromMinutes(1));
        var decision = OperationLeasePolicy.Acquire("build", "agent-b", now, TimeSpan.FromMinutes(5), now, existing);

        Assert.True(decision.Acquired);
        Assert.True(decision.ExpiredExistingLease);
        Assert.Equal("lease-reacquired", decision.ReasonCode);
    }
}
