using FlutterBuildDoctor.Application.Reliability;

namespace FlutterBuildDoctor.UnitTests.B450;

public sealed class OrchestrationQualityPolicyTests
{
    [Fact]
    public void Evaluate_SynthesizesMissingAndFailedRequiredBlockersWithSafeDurations()
    {
        var start = new DateTimeOffset(2026, 8, 11, 15, 0, 0, TimeSpan.FromHours(3));
        var result = OrchestrationQualityPolicy.Evaluate(
            new[] { "restore", "build", "test" },
            new[]
            {
                new OrchestrationPhaseEvidence("restore", OrchestrationPhaseState.Passed, true, start, start.AddSeconds(10), 1),
                new OrchestrationPhaseEvidence("build", OrchestrationPhaseState.Failed, true, start.AddSeconds(10), start.AddSeconds(30), 2)
            });

        Assert.Equal(2, result.BlockerCount);
        Assert.False(result.Successful);
        Assert.Equal(67, result.RequiredCompletionRate);
        Assert.Equal(25, result.QualityScore);
        Assert.Equal(OrchestrationPhaseState.Missing, result.Phases.First().State);
        Assert.Contains(result.Phases, phase => phase.Name == "build" && phase.Blocker);
        Assert.Equal(TimeSpan.FromSeconds(10), result.Phases.Single(phase => phase.Name == "restore").Duration);
        Assert.Equal(TimeSpan.Zero, result.Phases.Single(phase => phase.Name == "restore").StartedAtUtc!.Value.Offset);
        Assert.Equal(64, result.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_AllRequiredPassedAllowsSuccessAndIsDeterministic()
    {
        var now = DateTimeOffset.UtcNow;
        var evidence = new[]
        {
            new OrchestrationPhaseEvidence("build", OrchestrationPhaseState.Passed, true, now, now.AddSeconds(5), 2),
            new OrchestrationPhaseEvidence("restore", OrchestrationPhaseState.Passed, true, now.AddSeconds(-5), now, 1),
            new OrchestrationPhaseEvidence("optional", OrchestrationPhaseState.Skipped, false, Weight: 1)
        };

        var first = OrchestrationQualityPolicy.Evaluate(new[] { "restore", "build" }, evidence);
        var second = OrchestrationQualityPolicy.Evaluate(new[] { "build", "restore" }, evidence.Reverse());

        Assert.Equal(0, first.BlockerCount);
        Assert.True(first.Successful);
        Assert.Equal(100, first.RequiredCompletionRate);
        Assert.Equal(75, first.QualityScore);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void Evaluate_RejectsDuplicateOrInvalidPhaseIdentities()
    {
        Assert.Throws<ArgumentException>(() => OrchestrationQualityPolicy.Evaluate(new[] { "build", "BUILD" }, Array.Empty<OrchestrationPhaseEvidence>()));
        Assert.Throws<ArgumentException>(() => OrchestrationQualityPolicy.Evaluate(new[] { "bad phase" }, Array.Empty<OrchestrationPhaseEvidence>()));

        Assert.Throws<ArgumentException>(() => OrchestrationQualityPolicy.Evaluate(new[] { "build" }, new[]
        {
            new OrchestrationPhaseEvidence("build", OrchestrationPhaseState.Passed, true),
            new OrchestrationPhaseEvidence("BUILD", OrchestrationPhaseState.Passed, true)
        }));
    }

    [Fact]
    public void Evaluate_RejectsNegativeDuration()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(() => OrchestrationQualityPolicy.Evaluate(new[] { "build" }, new[]
        {
            new OrchestrationPhaseEvidence("build", OrchestrationPhaseState.Passed, true, now, now.AddSeconds(-1))
        }));
    }
}
