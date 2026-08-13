using System;
using System.Linq;
using FlutterBuildDoctor.Application.Governance;
using Xunit;

namespace FlutterBuildDoctor.UnitTests.B1550;

public sealed class DependencyLicenseEvidencePolicyTests
{
    [Fact]
    public void Evaluate_DeduplicatesClassifiesAndBlocksDeniedLicenses()
    {
        var evidence = new[]
        {
            new DependencyLicenseEvidence("pkg-a", "MIT"),
            new DependencyLicenseEvidence("pkg-a", "mit"),
            new DependencyLicenseEvidence("pkg-b", "Custom-1"),
            new DependencyLicenseEvidence("pkg-c", "GPL-3.0")
        };
        var decision = DependencyLicenseEvidencePolicy.Evaluate(evidence, new[] { "MIT", "Apache-2.0" }, new[] { "GPL-3.0" });
        Assert.Equal(3, decision.Findings.Count);
        Assert.True(decision.Blocked);
        Assert.Equal(1, decision.UnknownCount);
        Assert.Equal("denied", decision.Findings.Single(f => f.DependencyIdentity == "pkg-c").Classification);
        Assert.Equal("unknown", decision.Findings.Single(f => f.DependencyIdentity == "pkg-b").Classification);
        Assert.Equal("dependency-license-denied", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_RejectsEmptyAndConflictingLicenseSets()
    {
        Assert.Throws<ArgumentException>(() => DependencyLicenseEvidencePolicy.Evaluate(new[] { new DependencyLicenseEvidence("pkg", "") }, Array.Empty<string>(), Array.Empty<string>()));
        Assert.Throws<ArgumentException>(() => DependencyLicenseEvidencePolicy.Evaluate(Array.Empty<DependencyLicenseEvidence>(), new[] { "MIT" }, new[] { "mit" }));
    }
}
