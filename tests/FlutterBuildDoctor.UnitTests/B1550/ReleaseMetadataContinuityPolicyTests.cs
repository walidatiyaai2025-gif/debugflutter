using System;
using FlutterBuildDoctor.Application.Governance;
using Xunit;

namespace FlutterBuildDoctor.UnitTests.B1550;

public sealed class ReleaseMetadataContinuityPolicyTests
{
    private static readonly string CommitA = new('a', 40);
    private static readonly string CommitB = new('b', 40);

    [Fact]
    public void Evaluate_NormalizesAndAcceptsMonotonicMetadata()
    {
        var previous = new ReleaseMetadataRecord("release-1", "STABLE", 10, CommitA.ToUpperInvariant(), new DateTimeOffset(2026, 8, 12, 9, 0, 0, TimeSpan.FromHours(3)));
        var current = new ReleaseMetadataRecord(" Release-2 ", "stable", 11, CommitB, new DateTimeOffset(2026, 8, 13, 9, 0, 0, TimeSpan.FromHours(3)));
        var decision = ReleaseMetadataContinuityPolicy.Evaluate(current, previous);
        Assert.True(decision.Continuous);
        Assert.Empty(decision.Findings);
        Assert.Equal("release-2", decision.Current.ReleaseIdentity);
        Assert.Equal("stable", decision.Current.Channel);
        Assert.Equal(TimeSpan.Zero, decision.Current.CreatedAtUtc.Offset);
        Assert.Equal("release-metadata-continuous", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_DetectsDiscontinuityAndRejectsInvalidInput()
    {
        var now = DateTimeOffset.UtcNow;
        var previous = new ReleaseMetadataRecord("r1", "stable", 10, CommitA, now);
        var current = new ReleaseMetadataRecord("r2", "beta", 9, CommitB, now.AddMinutes(-1));
        var decision = ReleaseMetadataContinuityPolicy.Evaluate(current, previous);
        Assert.False(decision.Continuous);
        Assert.Equal(new[] { "build-not-increasing", "channel-changed", "timestamp-regressed" }, decision.Findings);
        Assert.Throws<ArgumentOutOfRangeException>(() => ReleaseMetadataContinuityPolicy.Evaluate(new ReleaseMetadataRecord("r", "stable", -1, CommitA, now)));
        Assert.Throws<ArgumentException>(() => ReleaseMetadataContinuityPolicy.Evaluate(new ReleaseMetadataRecord("r", "preview", 1, CommitA, now)));
        Assert.Throws<ArgumentException>(() => ReleaseMetadataContinuityPolicy.Evaluate(new ReleaseMetadataRecord("r", "stable", 1, "bad", now)));
    }
}
