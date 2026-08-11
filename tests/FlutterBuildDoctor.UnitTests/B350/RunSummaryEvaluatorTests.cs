using FlutterBuildDoctor.Application.Reliability;

namespace FlutterBuildDoctor.UnitTests.B350;

public sealed class RunSummaryEvaluatorTests
{
    [Fact]
    public void Evaluate_SynthesizesMissingRequiredPhaseAsBlockerAndNormalizesUtc()
    {
        var completedAt = new DateTimeOffset(2026, 8, 11, 9, 0, 0, TimeSpan.FromHours(3));
        var decision = RunSummaryEvaluator.Evaluate(
            new[] { "restore", "build", "test" },
            new[]
            {
                new RunPhaseEvidence("restore", RunPhaseState.Passed, true, 1),
                new RunPhaseEvidence("build", RunPhaseState.Passed, true, 2),
                new RunPhaseEvidence("optional", RunPhaseState.Skipped, false, 1)
            },
            completedAt);

        Assert.Equal(1, decision.BlockerCount);
        Assert.False(decision.Successful);
        Assert.Equal(67, decision.RequiredPassRate);
        Assert.Equal(75, decision.QualityScore);
        Assert.Equal(RunPhaseState.Missing, decision.Phases[0].State);
        Assert.Equal("test", decision.Phases[0].Name);
        Assert.Equal(TimeSpan.Zero, decision.CompletedAtUtc.Offset);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_FailedAndSkippedRequiredPhasesAreBlockers()
    {
        var decision = RunSummaryEvaluator.Evaluate(
            new[] { "build", "test" },
            new[]
            {
                new RunPhaseEvidence("build", RunPhaseState.Failed, true),
                new RunPhaseEvidence("test", RunPhaseState.Skipped, true)
            },
            DateTimeOffset.UtcNow);

        Assert.Equal(2, decision.BlockerCount);
        Assert.Equal(0, decision.RequiredPassRate);
        Assert.Equal(0, decision.QualityScore);
        Assert.False(decision.Successful);
    }

    [Fact]
    public void Evaluate_AllRequiredPassedAllowsSuccessfulRunAndPreservesOptionalSkip()
    {
        var decision = RunSummaryEvaluator.Evaluate(
            new[] { "restore", "build", "test" },
            new[]
            {
                new RunPhaseEvidence("test", RunPhaseState.Passed, true, 2),
                new RunPhaseEvidence("restore", RunPhaseState.Passed, true, 1),
                new RunPhaseEvidence("build", RunPhaseState.Passed, true, 3),
                new RunPhaseEvidence("publish", RunPhaseState.Skipped, false, 1)
            },
            DateTimeOffset.UtcNow);

        Assert.Equal(0, decision.BlockerCount);
        Assert.Equal(100, decision.RequiredPassRate);
        Assert.Equal(86, decision.QualityScore);
        Assert.True(decision.Successful);
        Assert.Equal(RunPhaseState.Skipped, decision.Phases.Single(item => item.Name == "publish").State);
    }

    [Fact]
    public void Evaluate_OrdersAndFingerprintsDeterministicallyAcrossInputOrder()
    {
        var completedAt = DateTimeOffset.UtcNow;
        var required = new[] { "build", "test" };
        var evidence = new[]
        {
            new RunPhaseEvidence("build", RunPhaseState.Passed, true),
            new RunPhaseEvidence("test", RunPhaseState.Passed, true)
        };

        var first = RunSummaryEvaluator.Evaluate(required, evidence, completedAt);
        var second = RunSummaryEvaluator.Evaluate(
            required.AsEnumerable().Reverse(),
            evidence.AsEnumerable().Reverse(),
            completedAt);

        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(first.Phases.Select(item => item.Name), second.Phases.Select(item => item.Name));
    }

    [Fact]
    public void Evaluate_RejectsInvalidAndDuplicatePhaseNames()
    {
        Assert.Throws<ArgumentException>(() => RunSummaryEvaluator.Evaluate(
            new[] { "bad phase" },
            Array.Empty<RunPhaseEvidence>(),
            DateTimeOffset.UtcNow));

        Assert.Throws<ArgumentException>(() => RunSummaryEvaluator.Evaluate(
            new[] { "build", "BUILD" },
            Array.Empty<RunPhaseEvidence>(),
            DateTimeOffset.UtcNow));
    }
}
