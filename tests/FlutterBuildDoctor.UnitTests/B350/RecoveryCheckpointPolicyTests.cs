using FlutterBuildDoctor.Application.Recovery;

namespace FlutterBuildDoctor.UnitTests.B350;

public sealed class RecoveryCheckpointPolicyTests
{
    [Fact]
    public void Evaluate_NormalizesUtcOrdersBoundsAndPrefersNewestRestorableCheckpoint()
    {
        var now = new DateTimeOffset(2026, 8, 11, 9, 0, 0, TimeSpan.FromHours(3));
        var source = new[]
        {
            new RecoveryCheckpoint(" old ", now.AddMinutes(-20), " Build ", true),
            new RecoveryCheckpoint("new", now.AddMinutes(-5), " Test ", true),
            new RecoveryCheckpoint("skip", now.AddMinutes(-1), "Publish", false)
        };

        var decision = RecoveryCheckpointPolicy.Evaluate(source, now, TimeSpan.FromHours(1), maxHistory: 2);

        Assert.Equal(2, decision.History.Count);
        Assert.Equal("new", decision.Preferred?.Id);
        Assert.Equal("test", decision.Preferred?.LastSuccessfulPhase);
        Assert.Equal(TimeSpan.Zero, decision.Preferred?.CreatedAt.Offset);
        Assert.Equal("checkpoint-ready", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_RejectsDuplicateCheckpointIdsCaseInsensitively()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(() => RecoveryCheckpointPolicy.Evaluate(
            new[]
            {
                new RecoveryCheckpoint("cp", now, "build"),
                new RecoveryCheckpoint("CP", now.AddSeconds(-1), "test")
            },
            now,
            TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Evaluate_DetectsStaleAndNonRestorableHistories()
    {
        var now = DateTimeOffset.UtcNow;
        var stale = RecoveryCheckpointPolicy.Evaluate(
            new[] { new RecoveryCheckpoint("cp", now.AddDays(-2), "build", true) },
            now,
            TimeSpan.FromHours(1));
        var nonRestorable = RecoveryCheckpointPolicy.Evaluate(
            new[] { new RecoveryCheckpoint("cp", now, "build", false) },
            now,
            TimeSpan.FromHours(1));

        Assert.Null(stale.Preferred);
        Assert.Equal("restorable-checkpoints-stale", stale.ReasonCode);
        Assert.Null(nonRestorable.Preferred);
        Assert.Equal("no-restorable-checkpoint", nonRestorable.ReasonCode);
    }

    [Fact]
    public void IsStale_UsesUtcAgeAndPositiveBound()
    {
        var checkpoint = new RecoveryCheckpoint("cp", new DateTimeOffset(2026, 8, 11, 6, 0, 0, TimeSpan.Zero), "build");
        var now = new DateTimeOffset(2026, 8, 11, 10, 0, 1, TimeSpan.FromHours(3));

        Assert.True(RecoveryCheckpointPolicy.IsStale(checkpoint, now, TimeSpan.FromHours(1)));
        Assert.False(RecoveryCheckpointPolicy.IsStale(checkpoint, now, TimeSpan.FromHours(2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => RecoveryCheckpointPolicy.IsStale(checkpoint, now, TimeSpan.Zero));
    }

    [Fact]
    public void Evaluate_IsDeterministicAcrossInputOrder()
    {
        var now = DateTimeOffset.UtcNow;
        var source = new[]
        {
            new RecoveryCheckpoint("b", now.AddMinutes(-2), "build"),
            new RecoveryCheckpoint("a", now.AddMinutes(-1), "test")
        };

        var first = RecoveryCheckpointPolicy.Evaluate(source, now, TimeSpan.FromHours(1));
        var second = RecoveryCheckpointPolicy.Evaluate(source.AsEnumerable().Reverse(), now, TimeSpan.FromHours(1));

        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(first.Preferred?.Id, second.Preferred?.Id);
    }
}
