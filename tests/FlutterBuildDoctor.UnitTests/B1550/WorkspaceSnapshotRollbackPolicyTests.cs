using System;
using System.Linq;
using FlutterBuildDoctor.Application.Governance;
using Xunit;

namespace FlutterBuildDoctor.UnitTests.B1550;

public sealed class WorkspaceSnapshotRollbackPolicyTests
{
    private static readonly string HashA = new('a', 64);
    private static readonly string HashB = new('b', 64);

    [Fact]
    public void Evaluate_SelectsLatestEligibleAndTracksProtected()
    {
        var now = new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
        var snapshots = new[]
        {
            new WorkspaceSnapshot("old", now.AddHours(-2), HashA, true),
            new WorkspaceSnapshot("latest", now.AddMinutes(-5), HashB, false),
            new WorkspaceSnapshot("future-near", now.AddMinutes(1), HashA, true)
        };
        var decision = WorkspaceSnapshotRollbackPolicy.Evaluate(snapshots, now, TimeSpan.FromMinutes(2));
        Assert.Equal("latest", decision.SelectedSnapshotIdentity);
        Assert.Equal(new[] { "latest", "old" }, decision.EligibleSnapshots.Select(s => s.Identity).ToArray());
        Assert.Equal(2, decision.ProtectedCount);
        Assert.Equal("workspace-rollback-ready", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_ClampsAndRejectsInvalidSnapshots()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(WorkspaceSnapshotRollbackPolicy.MaxFutureTolerance, WorkspaceSnapshotRollbackPolicy.Evaluate(Array.Empty<WorkspaceSnapshot>(), now, TimeSpan.FromHours(1)).FutureTolerance);
        Assert.Throws<ArgumentException>(() => WorkspaceSnapshotRollbackPolicy.Evaluate(new[] { new WorkspaceSnapshot("a", now, HashA, false), new WorkspaceSnapshot("a", now, HashB, false) }, now, TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => WorkspaceSnapshotRollbackPolicy.Evaluate(new[] { new WorkspaceSnapshot("future", now.AddMinutes(10), HashA, false) }, now, TimeSpan.FromMinutes(1)));
        Assert.Throws<ArgumentException>(() => WorkspaceSnapshotRollbackPolicy.Evaluate(new[] { new WorkspaceSnapshot("bad", now, "bad", false) }, now, TimeSpan.Zero));
    }
}
