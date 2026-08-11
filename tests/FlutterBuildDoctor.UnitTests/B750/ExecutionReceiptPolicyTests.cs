using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B750;

public sealed class ExecutionReceiptPolicyTests
{
    [Fact]
    public void Evaluate_NormalizesMonotonicPhasesAndProducesCanonicalSummary()
    {
        var start = new DateTimeOffset(2026, 8, 11, 20, 0, 0, TimeSpan.FromHours(3));
        var phases = new[]
        {
            new ExecutionReceiptPhase(" Restore ", start, start.AddSeconds(2), ExecutionReceiptPhaseStatus.Success),
            new ExecutionReceiptPhase("Build", start.AddSeconds(2), start.AddSeconds(5), ExecutionReceiptPhaseStatus.Success),
            new ExecutionReceiptPhase("Test", start.AddSeconds(5), start.AddSeconds(9), ExecutionReceiptPhaseStatus.Failure)
        };

        var first = ExecutionReceiptPolicy.Evaluate(" RUN-001 ", phases);
        var second = ExecutionReceiptPolicy.Evaluate("run-001", phases);

        Assert.Equal("run-001", first.Identity);
        Assert.All(first.Phases, phase => Assert.Equal(TimeSpan.Zero, phase.StartedAtUtc.Offset));
        Assert.Contains("restore:success", first.PhaseSummary, StringComparison.Ordinal);
        Assert.Contains("test:failure", first.PhaseSummary, StringComparison.Ordinal);
        Assert.Equal("execution-receipt-valid", first.ReasonCode);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void Evaluate_RejectsNegativePhaseDuration()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(() => ExecutionReceiptPolicy.Evaluate("run", new[]
        {
            new ExecutionReceiptPhase("build", now, now.AddSeconds(-1), ExecutionReceiptPhaseStatus.Failure)
        }));
    }

    [Fact]
    public void Evaluate_RejectsOverlappingOrOutOfOrderPhases()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(() => ExecutionReceiptPolicy.Evaluate("run", new[]
        {
            new ExecutionReceiptPhase("restore", now, now.AddSeconds(5), ExecutionReceiptPhaseStatus.Success),
            new ExecutionReceiptPhase("build", now.AddSeconds(4), now.AddSeconds(8), ExecutionReceiptPhaseStatus.Success)
        }));
    }

    [Fact]
    public void Evaluate_RejectsEmptyPhaseCollection()
        => Assert.Throws<ArgumentOutOfRangeException>(() => ExecutionReceiptPolicy.Evaluate("run", Array.Empty<ExecutionReceiptPhase>()));
}
