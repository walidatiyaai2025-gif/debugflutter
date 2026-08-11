using FlutterBuildDoctor.Application.Diagnostics;

namespace FlutterBuildDoctor.UnitTests.B250;

public sealed class DiagnosticSessionPlannerTests
{
    [Fact]
    public void Create_GeneratesIdentity_NormalizesUtc_OrdersAndFingerprintsDeterministically()
    {
        var started = new DateTimeOffset(2026, 8, 11, 9, 0, 0, TimeSpan.FromHours(3));
        var steps = new[]
        {
            new DiagnosticStep("zeta", 1, DiagnosticStepPriority.Normal),
            new DiagnosticStep("alpha", 2, DiagnosticStepPriority.Critical, true),
            new DiagnosticStep("beta", 3, DiagnosticStepPriority.Critical)
        };

        var first = DiagnosticSessionPlanner.Create(steps, started);
        var second = DiagnosticSessionPlanner.Create(steps.AsEnumerable().Reverse(), started);

        Assert.NotEqual(Guid.Empty, first.SessionId);
        Assert.Equal(TimeSpan.Zero, first.StartedAtUtc.Offset);
        Assert.Equal(new[] { "alpha", "beta", "zeta" }, first.Steps.Select(step => step.Id));
        Assert.Equal(64, first.Fingerprint.Length);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void Create_RejectsEmptyIdentityDuplicatesAndUnboundedStepCounts()
    {
        Assert.Throws<ArgumentException>(() => DiagnosticSessionPlanner.Create(
            new[] { new DiagnosticStep("same"), new DiagnosticStep("SAME") },
            DateTimeOffset.UtcNow));

        Assert.Throws<ArgumentException>(() => DiagnosticSessionPlanner.Create(
            new[] { new DiagnosticStep("one") },
            DateTimeOffset.UtcNow,
            Guid.Empty));

        Assert.Throws<ArgumentOutOfRangeException>(() => DiagnosticSessionPlanner.Create(
            Enumerable.Range(0, DiagnosticSessionPlanner.MaxSteps + 1).Select(index => new DiagnosticStep($"step-{index}")),
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void EvaluateProgress_UsesWeightsAndStopsOnBlockerOrCancellation()
    {
        var plan = DiagnosticSessionPlanner.Create(
            new[]
            {
                new DiagnosticStep("blocker", 1, DiagnosticStepPriority.Critical, true),
                new DiagnosticStep("heavy", 3)
            },
            DateTimeOffset.UtcNow);

        var progress = DiagnosticSessionPlanner.EvaluateProgress(
            plan,
            new[]
            {
                new DiagnosticStepState("blocker", Completed: true, Failed: true),
                new DiagnosticStepState("heavy", Completed: false)
            });

        Assert.Equal(25, progress.Percent);
        Assert.True(progress.HasBlockerFailure);
        Assert.True(progress.ShouldStop);
        Assert.False(progress.IsCancelled);

        var cancelled = DiagnosticSessionPlanner.EvaluateProgress(plan, Array.Empty<DiagnosticStepState>(), isCancelled: true);
        Assert.True(cancelled.IsCancelled);
        Assert.True(cancelled.ShouldStop);
    }

    [Fact]
    public void IsEvidenceStale_UsesUtcAgeAndPositiveBound()
    {
        var captured = new DateTimeOffset(2026, 8, 11, 6, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 8, 11, 10, 0, 1, TimeSpan.FromHours(3));

        Assert.True(DiagnosticSessionPlanner.IsEvidenceStale(captured, now, TimeSpan.FromHours(1)));
        Assert.False(DiagnosticSessionPlanner.IsEvidenceStale(captured, now, TimeSpan.FromHours(2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiagnosticSessionPlanner.IsEvidenceStale(captured, now, TimeSpan.Zero));
    }
}
