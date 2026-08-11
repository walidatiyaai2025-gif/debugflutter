using FlutterBuildDoctor.Application.Workspaces;

namespace FlutterBuildDoctor.UnitTests.B550;

public sealed class WorkspaceLockPolicyTests
{
    [Fact]
    public void Evaluate_AllowsAvailableLockNormalizesUtcAndFingerprintsDeterministically()
    {
        var now = new DateTimeOffset(2026, 8, 11, 16, 0, 0, TimeSpan.FromHours(3));
        var request = new WorkspaceLock("lock-1", "owner-1", @"C:\work\demo\", now, TimeSpan.FromSeconds(1));

        var first = WorkspaceLockPolicy.Evaluate(request, null, now);
        var second = WorkspaceLockPolicy.Evaluate(request, null, now);

        Assert.True(first.Allowed);
        Assert.Equal("lock-available", first.ReasonCode);
        Assert.Equal(TimeSpan.Zero, first.Requested.AcquiredAt.Offset);
        Assert.Equal(WorkspaceLockPolicy.MinLease, first.Requested.Lease);
        Assert.Equal(@"C:\work\demo", first.Requested.WorkspacePath);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_PrefersActiveOwnerAndRejectsConflictingOwner()
    {
        var now = DateTimeOffset.UtcNow;
        var existing = new WorkspaceLock("lock-existing", "owner-1", @"C:\work\demo", now.AddMinutes(-1), TimeSpan.FromMinutes(10));
        var same = WorkspaceLockPolicy.Evaluate(new WorkspaceLock("lock-new", "owner-1", @"C:\work\demo", now, TimeSpan.FromMinutes(5)), existing, now);
        var conflict = WorkspaceLockPolicy.Evaluate(new WorkspaceLock("lock-new", "owner-2", @"C:\work\demo", now, TimeSpan.FromMinutes(5)), existing, now);

        Assert.True(same.Allowed);
        Assert.True(same.ExistingOwner);
        Assert.Equal("active-owner-lock", same.ReasonCode);
        Assert.False(conflict.Allowed);
        Assert.Equal("active-lock-conflict", conflict.ReasonCode);
    }

    [Fact]
    public void Evaluate_AllowsReplacementOfExpiredLockAndClampsLease()
    {
        var now = DateTimeOffset.UtcNow;
        var existing = new WorkspaceLock("old", "owner-old", @"C:\work\demo", now.AddHours(-5), TimeSpan.FromMinutes(5));
        var decision = WorkspaceLockPolicy.Evaluate(new WorkspaceLock("new", "owner-new", @"C:\work\demo", now, TimeSpan.FromDays(1)), existing, now);

        Assert.True(decision.Allowed);
        Assert.True(decision.ExistingExpired);
        Assert.Equal("expired-lock-replace", decision.ReasonCode);
        Assert.Equal(WorkspaceLockPolicy.MaxLease, decision.Requested.Lease);
    }

    [Theory]
    [InlineData("bad identity")]
    [InlineData("bad/identity")]
    public void Normalize_RejectsInvalidIdentities(string value)
        => Assert.Throws<ArgumentException>(() => WorkspaceLockPolicy.Normalize(new WorkspaceLock(value, "owner", @"C:\work", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1))));

    [Fact]
    public void NormalizeWorkspacePath_RejectsRelativePath()
        => Assert.Throws<ArgumentException>(() => WorkspaceLockPolicy.NormalizeWorkspacePath("relative\\work"));
}
