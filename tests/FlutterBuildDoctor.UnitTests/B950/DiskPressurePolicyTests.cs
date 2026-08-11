using System;
using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B950;

public sealed class DiskPressurePolicyTests
{
    [Fact]
    public void Evaluate_ClassifiesPressureAndComputesReclaimTarget()
    {
        var healthy = DiskPressurePolicy.Evaluate(" C: ", 1_000, 500, 20, 10);
        var warning = DiskPressurePolicy.Evaluate("c:", 1_000, 150, 20, 10);
        var critical = DiskPressurePolicy.Evaluate("c:", 1_000, 50, 20, 10);

        Assert.Equal(DiskPressureLevel.Healthy, healthy.Level);
        Assert.Equal("disk-pressure-healthy", healthy.ReasonCode);
        Assert.Equal(DiskPressureLevel.Warning, warning.Level);
        Assert.Equal("disk-pressure-warning", warning.ReasonCode);
        Assert.True(warning.ReclaimTargetBytes > 0);
        Assert.Equal(DiskPressureLevel.Critical, critical.Level);
        Assert.Equal("disk-pressure-critical", critical.ReasonCode);
        Assert.Equal(64, critical.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_ClampsThresholds()
    {
        var decision = DiskPressurePolicy.Evaluate("disk0", 1_000, 600, 99, 1);

        Assert.Equal(50, decision.WarningPercent);
        Assert.Equal(1, decision.CriticalPercent);
        Assert.InRange(decision.FreePercent, 0, 100);
    }

    [Fact]
    public void Evaluate_RejectsInvalidMetricsAndThresholdOrdering()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DiskPressurePolicy.Evaluate("disk", 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiskPressurePolicy.Evaluate("disk", 100, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiskPressurePolicy.Evaluate("disk", 100, 101));
        Assert.Throws<ArgumentException>(() => DiskPressurePolicy.Evaluate("disk", 100, 50, 10, 10));
    }
}
