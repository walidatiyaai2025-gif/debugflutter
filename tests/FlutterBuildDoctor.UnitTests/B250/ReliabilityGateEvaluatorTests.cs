using FlutterBuildDoctor.Application.Reliability;

namespace FlutterBuildDoctor.UnitTests.B250;

public sealed class ReliabilityGateEvaluatorTests
{
    [Fact]
    public void Evaluate_SynthesizesMissingRequiredGateAsBlockerAndOrdersDeterministically()
    {
        var decision = ReliabilityGateEvaluator.Evaluate(
            new[] { "build", "tests", "security" },
            new[]
            {
                new ReliabilityGateEvidence("tests", ReliabilityGateState.Passed, Required: true, Weight: 2),
                new ReliabilityGateEvidence("build", ReliabilityGateState.Passed, Required: true, Weight: 3),
                new ReliabilityGateEvidence("optional", ReliabilityGateState.Skipped, Required: false)
            });

        Assert.Equal(4, decision.Gates.Count);
        Assert.Equal("security", decision.Gates[0].Name);
        Assert.Equal(ReliabilityGateState.Missing, decision.Gates[0].State);
        Assert.Equal(ReliabilityGateSeverity.Blocker, decision.Gates[0].Severity);
        Assert.Equal(67, decision.RequiredPassRate);
        Assert.Equal(83, decision.ReadinessScore);
        Assert.Equal(1, decision.BlockerCount);
        Assert.False(decision.ReleaseEligible);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_FailedAndSkippedRequiredGatesAreBlockers()
    {
        var decision = ReliabilityGateEvaluator.Evaluate(
            new[] { "build", "tests" },
            new[]
            {
                new ReliabilityGateEvidence("build", ReliabilityGateState.Failed, Required: true),
                new ReliabilityGateEvidence("tests", ReliabilityGateState.Skipped, Required: true)
            });

        Assert.Equal(2, decision.BlockerCount);
        Assert.All(decision.Gates, gate => Assert.Equal(ReliabilityGateSeverity.Blocker, gate.Severity));
        Assert.Equal(0, decision.RequiredPassRate);
        Assert.Equal(0, decision.ReadinessScore);
        Assert.False(decision.ReleaseEligible);
    }

    [Fact]
    public void Evaluate_AllRequiredPassedAllowsReleaseAndWeightsReadiness()
    {
        var decision = ReliabilityGateEvaluator.Evaluate(
            new[] { "build", "tests" },
            new[]
            {
                new ReliabilityGateEvidence("tests", ReliabilityGateState.Passed, Required: true, Weight: 1),
                new ReliabilityGateEvidence("build", ReliabilityGateState.Passed, Required: true, Weight: 3),
                new ReliabilityGateEvidence("lint", ReliabilityGateState.Failed, Required: false, Weight: 1)
            });

        Assert.Equal(100, decision.RequiredPassRate);
        Assert.Equal(80, decision.ReadinessScore);
        Assert.Equal(0, decision.BlockerCount);
        Assert.True(decision.ReleaseEligible);
        Assert.Equal(ReliabilityGateSeverity.Warning, decision.Gates.Single(gate => gate.Name == "lint").Severity);
    }

    [Fact]
    public void Evaluate_IsDeterministicAcrossEvidenceOrder()
    {
        var required = new[] { "build", "tests" };
        var evidence = new[]
        {
            new ReliabilityGateEvidence("build", ReliabilityGateState.Passed, true),
            new ReliabilityGateEvidence("tests", ReliabilityGateState.Passed, true)
        };

        var first = ReliabilityGateEvaluator.Evaluate(required, evidence);
        var second = ReliabilityGateEvaluator.Evaluate(required.Reverse(), evidence.Reverse());

        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(first.Gates.Select(gate => gate.Name), second.Gates.Select(gate => gate.Name));
    }

    [Fact]
    public void Evaluate_RejectsInvalidRequiredNamesAndDuplicateEvidence()
    {
        Assert.Throws<ArgumentException>(() => ReliabilityGateEvaluator.Evaluate(Array.Empty<string>(), Array.Empty<ReliabilityGateEvidence>()));
        Assert.Throws<ArgumentException>(() => ReliabilityGateEvaluator.Evaluate(
            new[] { "build" },
            new[]
            {
                new ReliabilityGateEvidence("build", ReliabilityGateState.Passed, true),
                new ReliabilityGateEvidence("BUILD", ReliabilityGateState.Passed, true)
            }));
    }
}
