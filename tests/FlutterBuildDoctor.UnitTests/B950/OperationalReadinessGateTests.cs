using System;
using System.Linq;
using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B950;

public sealed class OperationalReadinessGateTests
{
    [Fact]
    public void Evaluate_NormalizesScoresAndPassesWhenMandatoryChecksPass()
    {
        var first = OperationalReadinessGate.Evaluate(new[]
        {
            new OperationalReadinessCheck(" Tests ", "Quality", true, true, 3),
            new OperationalReadinessCheck("build", "quality", true, true, 2),
            new OperationalReadinessCheck("telemetry", "Optional", false, false, 1)
        }, new[] { "build", "tests" });
        var second = OperationalReadinessGate.Evaluate(new[]
        {
            new OperationalReadinessCheck("telemetry", "optional", false, false, 1),
            new OperationalReadinessCheck("BUILD", "QUALITY", true, true, 2),
            new OperationalReadinessCheck("tests", "quality", true, true, 3)
        }, new[] { "TESTS", "BUILD" });

        Assert.True(first.Ready);
        Assert.Empty(first.MandatoryBlockers);
        Assert.Equal(83, first.Score);
        Assert.Equal("operational-readiness-ready", first.ReasonCode);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(new[] { "telemetry", "build", "tests" }, first.Checks.Select(check => check.Identity));
    }

    [Fact]
    public void Evaluate_BlocksFailedAndMissingMandatoryChecksDeterministically()
    {
        var decision = OperationalReadinessGate.Evaluate(new[]
        {
            new OperationalReadinessCheck("build", "quality", true, false, 5),
            new OperationalReadinessCheck("tests", "quality", false, true, 5)
        }, new[] { "build", "tests", "signing" });

        Assert.False(decision.Ready);
        Assert.Equal(new[] { "failed:build", "missing-mandatory:signing", "missing-mandatory:tests" }, decision.MandatoryBlockers);
        Assert.Equal(50, decision.Score);
        Assert.Equal("operational-readiness-blocked", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_RejectsDuplicatesAndBoundsCheckCount()
    {
        Assert.Throws<ArgumentException>(() => OperationalReadinessGate.Evaluate(new[]
        {
            new OperationalReadinessCheck("build", "quality", true, true),
            new OperationalReadinessCheck("BUILD", "quality", true, true)
        }));

        Assert.Throws<ArgumentOutOfRangeException>(() => OperationalReadinessGate.Evaluate(new[]
        {
            new OperationalReadinessCheck("build", "quality", true, true),
            new OperationalReadinessCheck("tests", "quality", true, true)
        }, maxChecks: 1));
    }

    [Fact]
    public void Evaluate_ClampsWeightsAndReturnsPerfectScoreForEmptyOptionalSet()
    {
        var weighted = OperationalReadinessGate.Evaluate(new[]
        {
            new OperationalReadinessCheck("build", "quality", false, true, 500)
        });
        var empty = OperationalReadinessGate.Evaluate(Array.Empty<OperationalReadinessCheck>());

        Assert.Equal(100, Assert.Single(weighted.Checks).Weight);
        Assert.Equal(100, weighted.Score);
        Assert.True(empty.Ready);
        Assert.Equal(100, empty.Score);
    }
}
