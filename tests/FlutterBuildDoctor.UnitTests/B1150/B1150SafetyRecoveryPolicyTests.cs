using System;
using System.Linq;
using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B1150;

public sealed class PortAllocationSafetyPolicyTests
{
    [Fact]
    public void Evaluate_NormalizesPurposeAndKeepsAvailableRequestedPort()
    {
        var decision = PortAllocationSafetyPolicy.Evaluate(" Debug-Server ", 5000, new[] { 5002 }, new[] { 5003 });
        Assert.Equal("debug-server", decision.Purpose);
        Assert.True(decision.RequestedAvailable);
        Assert.Equal(5000, decision.AllocatedPort);
        Assert.Equal("port-allocation-requested-available", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_SelectsDeterministicFallbackAroundBlockedPorts()
    {
        var first = PortAllocationSafetyPolicy.Evaluate("debug", 5000, new[] { 5000 }, new[] { 5001 });
        var second = PortAllocationSafetyPolicy.Evaluate("DEBUG", 5000, new[] { 5000 }, new[] { 5001 });
        Assert.False(first.RequestedAvailable);
        Assert.Equal(5002, first.AllocatedPort);
        Assert.Equal("port-allocation-fallback-selected", first.ReasonCode);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void Evaluate_RejectsDuplicatesAndInvalidRanges()
    {
        Assert.Throws<ArgumentException>(() => PortAllocationSafetyPolicy.Evaluate("debug", 5000, new[] { 5001, 5001 }, Array.Empty<int>()));
        Assert.Throws<ArgumentException>(() => PortAllocationSafetyPolicy.Evaluate("debug", 5000, Array.Empty<int>(), new[] { 5001, 5001 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => PortAllocationSafetyPolicy.Evaluate("debug", 80, Array.Empty<int>(), Array.Empty<int>()));
    }
}

public sealed class DeviceCapabilityRequirementPolicyTests
{
    [Fact]
    public void Evaluate_SurfacesMissingAndInsufficientCapabilitiesWithScore()
    {
        var decision = DeviceCapabilityRequirementPolicy.Evaluate(
            new[] { new DeviceCapabilityObservation(" Camera ", 2), new DeviceCapabilityObservation("GPU", 4) },
            new[] { new DeviceCapabilityRequirement("camera", 1), new DeviceCapabilityRequirement("gpu", 5), new DeviceCapabilityRequirement("storage", 1) });
        Assert.Equal(new[] { "camera", "gpu" }, decision.Observations.Select(item => item.Name));
        Assert.Equal(new[] { "insufficient:gpu:4/5", "missing:storage" }, decision.Blockers);
        Assert.Equal(60, decision.Score);
        Assert.Equal("device-capabilities-blocked", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_QualifiesEmptyRequirementsAndRejectsDuplicatesOrNegativeLevels()
    {
        var empty = DeviceCapabilityRequirementPolicy.Evaluate(Array.Empty<DeviceCapabilityObservation>(), Array.Empty<DeviceCapabilityRequirement>());
        Assert.Equal(100, empty.Score);
        Assert.Equal("device-capabilities-satisfied", empty.ReasonCode);
        Assert.Throws<ArgumentException>(() => DeviceCapabilityRequirementPolicy.Evaluate(new[] { new DeviceCapabilityObservation("gpu", 1), new DeviceCapabilityObservation("GPU", 2) }, Array.Empty<DeviceCapabilityRequirement>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => DeviceCapabilityRequirementPolicy.Evaluate(new[] { new DeviceCapabilityObservation("gpu", -1) }, Array.Empty<DeviceCapabilityRequirement>()));
    }
}

public sealed class ResourceReservationPolicyTests
{
    [Fact]
    public void Evaluate_ComputesReservedCapacityAndGrantsSafeRequest()
    {
        var decision = ResourceReservationPolicy.Evaluate(" Build-1 ", new ResourceVector(10, 1000, 2000), new ResourceVector(8, 800, 1700), 10);
        Assert.Equal("build-1", decision.ReservationIdentity);
        Assert.Equal(new ResourceVector(9, 900, 1800), decision.Available);
        Assert.True(decision.Granted);
        Assert.Equal("resource-reservation-granted", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_RejectsOversubscriptionAndNegativeMetricsWhileClampingReserve()
    {
        var rejected = ResourceReservationPolicy.Evaluate("build", new ResourceVector(10, 1000, 1000), new ResourceVector(2, 200, 200), 200);
        Assert.Equal(90, rejected.SafetyReservePercent);
        Assert.False(rejected.Granted);
        Assert.Equal("resource-reservation-rejected", rejected.ReasonCode);
        Assert.Throws<ArgumentOutOfRangeException>(() => ResourceReservationPolicy.Evaluate("build", new ResourceVector(-1, 0, 0), new ResourceVector(0, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ResourceReservationPolicy.Evaluate("build", new ResourceVector(1, 1, 1), new ResourceVector(0, -1, 0)));
    }
}

public sealed class ArtifactSignatureEvidencePolicyTests
{
    private static readonly string Digest = new('a', 64);

    [Fact]
    public void Evaluate_NormalizesEvidenceAndQualifiesTrustedFreshSignature()
    {
        var observed = new DateTimeOffset(2026, 8, 13, 0, 0, 0, TimeSpan.Zero);
        var decision = ArtifactSignatureEvidencePolicy.Evaluate(" APK-1 ", " Vendor ", " SHA256-RSA ", Digest.ToUpperInvariant(), observed.AddDays(-1), observed, true, true);
        Assert.Equal("apk-1", decision.ArtifactIdentity);
        Assert.Equal("vendor", decision.SignerIdentity);
        Assert.Equal("sha256-rsa", decision.Algorithm);
        Assert.Equal(Digest, decision.Digest);
        Assert.True(decision.Qualified);
        Assert.False(decision.Stale);
        Assert.Equal("signature-evidence-qualified", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_ClassifiesMissingUntrustedAndStaleEvidence()
    {
        var observed = DateTimeOffset.Parse("2026-08-13T00:00:00Z");
        Assert.Equal("signature-evidence-missing", ArtifactSignatureEvidencePolicy.Evaluate("apk", "vendor", "rsa", Digest, observed, observed, false, true).ReasonCode);
        Assert.Equal("signature-signer-untrusted", ArtifactSignatureEvidencePolicy.Evaluate("apk", "vendor", "rsa", Digest, observed, observed, true, false).ReasonCode);
        var stale = ArtifactSignatureEvidencePolicy.Evaluate("apk", "vendor", "rsa", Digest, observed.AddDays(-31), observed, true, true, TimeSpan.FromDays(30));
        Assert.True(stale.Stale);
        Assert.Equal("signature-evidence-stale", stale.ReasonCode);
        Assert.Throws<ArgumentException>(() => ArtifactSignatureEvidencePolicy.Evaluate("apk", "vendor", "rsa", "bad", observed, observed, true, true));
    }
}

public sealed class TestResultAggregationPolicyTests
{
    [Fact]
    public void Evaluate_AggregatesCountsAndBlocksMandatoryFailures()
    {
        var decision = TestResultAggregationPolicy.Evaluate(new[]
        {
            new TestSuiteAggregateInput(" Unit ", 10, 9, 1, 0, true),
            new TestSuiteAggregateInput("integration", 5, 4, 0, 1, false)
        });
        Assert.Equal(15, decision.Total);
        Assert.Equal(13, decision.Passed);
        Assert.Equal(1, decision.Failed);
        Assert.Equal(1, decision.Skipped);
        Assert.Equal(new[] { "failed:unit:1" }, decision.MandatoryBlockers);
        Assert.Equal(87, decision.PassPercentage);
        Assert.Equal("test-aggregate-blocked", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_HandlesEmptyAndRejectsDuplicatesOrBadArithmetic()
    {
        var empty = TestResultAggregationPolicy.Evaluate(Array.Empty<TestSuiteAggregateInput>());
        Assert.Equal(100, empty.PassPercentage);
        Assert.Equal("test-aggregate-qualified", empty.ReasonCode);
        Assert.Throws<ArgumentException>(() => TestResultAggregationPolicy.Evaluate(new[] { new TestSuiteAggregateInput("unit", 1, 1, 0, 0, true), new TestSuiteAggregateInput("UNIT", 1, 1, 0, 0, true) }));
        Assert.Throws<ArgumentException>(() => TestResultAggregationPolicy.Evaluate(new[] { new TestSuiteAggregateInput("unit", 2, 1, 0, 0, true) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => TestResultAggregationPolicy.Evaluate(new[] { new TestSuiteAggregateInput("unit", -1, 0, 0, 0, true) }));
    }
}

public sealed class SessionRecoveryCheckpointPolicyTests
{
    [Fact]
    public void Evaluate_SelectsLatestVerifiedCheckpointAndComputesReplayDistance()
    {
        var local = new DateTimeOffset(2026, 8, 13, 3, 0, 0, TimeSpan.FromHours(3));
        var decision = SessionRecoveryCheckpointPolicy.Evaluate(" Session-A ", new[]
        {
            new RecoveryCheckpoint("c1", 1, local.AddMinutes(-3), true),
            new RecoveryCheckpoint("c2", 2, local.AddMinutes(-2), true),
            new RecoveryCheckpoint("c3", 3, local.AddMinutes(-1), false)
        });
        Assert.Equal("session-a", decision.SessionIdentity);
        Assert.NotNull(decision.SelectedCheckpoint);
        Assert.Equal("c2", decision.SelectedCheckpoint!.Identity);
        Assert.Equal(TimeSpan.Zero, decision.SelectedCheckpoint.CapturedAt.Offset);
        Assert.Equal(1, decision.ReplayDistance);
        Assert.Equal("session-recovery-checkpoint-selected", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_ReportsMissingVerifiedCheckpointAndRejectsDuplicateSequences()
    {
        var none = SessionRecoveryCheckpointPolicy.Evaluate("session", new[] { new RecoveryCheckpoint("c1", 2, DateTimeOffset.UtcNow, false) });
        Assert.Null(none.SelectedCheckpoint);
        Assert.Equal(3, none.ReplayDistance);
        Assert.Equal("session-recovery-checkpoint-missing", none.ReasonCode);
        Assert.Throws<ArgumentException>(() => SessionRecoveryCheckpointPolicy.Evaluate("session", new[] { new RecoveryCheckpoint("c1", 1, DateTimeOffset.UtcNow, true), new RecoveryCheckpoint("c2", 1, DateTimeOffset.UtcNow, true) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => SessionRecoveryCheckpointPolicy.Evaluate("session", new[] { new RecoveryCheckpoint("c1", -1, DateTimeOffset.UtcNow, true) }));
    }
}

public sealed class BuildQueueFairnessPolicyTests
{
    [Fact]
    public void Evaluate_AppliesAgingAndKeepsExclusiveWorkFirst()
    {
        var now = DateTimeOffset.Parse("2026-08-13T00:00:00Z");
        var decision = BuildQueueFairnessPolicy.Evaluate(new[]
        {
            new BuildQueueItem("normal", "team-a", 90, now.AddMinutes(-1), false),
            new BuildQueueItem("exclusive", "team-b", 10, now.AddMinutes(-30), true),
            new BuildQueueItem("aged", "team-c", 20, now.AddMinutes(-25), false)
        }, now, TimeSpan.FromMinutes(5));
        Assert.Equal("exclusive", decision.RankedItems[0].Item.Identity);
        Assert.Equal(16, decision.RankedItems[0].EffectivePriority);
        Assert.Equal("normal", decision.RankedItems[1].Item.Identity);
        Assert.Equal("build-queue-ranked", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_NormalizesOwnerPriorityAndRejectsDuplicateItems()
    {
        var now = DateTimeOffset.UtcNow;
        var decision = BuildQueueFairnessPolicy.Evaluate(new[] { new BuildQueueItem("job", " Team-A ", 500, now, false) }, now);
        Assert.Equal("team-a", decision.RankedItems[0].Item.Owner);
        Assert.Equal(100, decision.RankedItems[0].Item.Priority);
        Assert.Throws<ArgumentException>(() => BuildQueueFairnessPolicy.Evaluate(new[] { new BuildQueueItem("job", "a", 1, now, false), new BuildQueueItem("JOB", "b", 2, now, false) }, now));
        Assert.Equal("build-queue-empty", BuildQueueFairnessPolicy.Evaluate(Array.Empty<BuildQueueItem>(), now).ReasonCode);
    }
}

public sealed class EndpointQuorumFallbackPolicyTests
{
    [Fact]
    public void Evaluate_CountsQuorumAndRanksHealthyFallbacksByLatency()
    {
        var decision = EndpointQuorumFallbackPolicy.Evaluate(new[]
        {
            new EndpointQuorumProbe("primary", true, 80, true),
            new EndpointQuorumProbe("mirror-b", true, 20, false),
            new EndpointQuorumProbe("mirror-a", true, 20, false)
        }, 2);
        Assert.Equal(3, decision.HealthyCount);
        Assert.Equal(2, decision.RequiredQuorum);
        Assert.True(decision.QuorumMet);
        Assert.Empty(decision.Blockers);
        Assert.Equal(new[] { "mirror-a", "mirror-b", "primary" }, decision.FallbackOrder.Select(item => item.Identity));
        Assert.Equal("endpoint-quorum-healthy", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_SurfacesMandatoryFailureAndRejectsDuplicateOrNegativeLatency()
    {
        var degraded = EndpointQuorumFallbackPolicy.Evaluate(new[] { new EndpointQuorumProbe("primary", false, 10, true), new EndpointQuorumProbe("backup", true, 50, false) }, 20);
        Assert.Equal(2, degraded.RequiredQuorum);
        Assert.False(degraded.QuorumMet);
        Assert.Equal(new[] { "mandatory-unhealthy:primary" }, degraded.Blockers);
        Assert.Equal("endpoint-quorum-degraded", degraded.ReasonCode);
        Assert.Throws<ArgumentException>(() => EndpointQuorumFallbackPolicy.Evaluate(new[] { new EndpointQuorumProbe("a", true, 1, false), new EndpointQuorumProbe("A", true, 2, false) }, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => EndpointQuorumFallbackPolicy.Evaluate(new[] { new EndpointQuorumProbe("a", true, -1, false) }, 1));
    }
}

public sealed class ReleaseRollbackEligibilityPolicyTests
{
    [Fact]
    public void Evaluate_NormalizesVersionsAndQualifiesSafeRollback()
    {
        var first = ReleaseRollbackEligibilityPolicy.Evaluate(" Rollback-1 ", "v2.3.0", "2.2.1-RC.1", true, true, true);
        var second = ReleaseRollbackEligibilityPolicy.Evaluate("rollback-1", "2.3.0", "2.2.1-rc.1", true, true, true);
        Assert.Equal("2.3.0", first.SourceVersion);
        Assert.Equal("2.2.1-rc.1", first.TargetVersion);
        Assert.True(first.Eligible);
        Assert.Empty(first.Blockers);
        Assert.Equal("release-rollback-eligible", first.ReasonCode);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void Evaluate_SurfacesAllRollbackBlockersAndRejectsSameVersion()
    {
        var blocked = ReleaseRollbackEligibilityPolicy.Evaluate("rollback", "2.0.0", "1.9.0", false, false, false);
        Assert.False(blocked.Eligible);
        Assert.Equal(new[] { "artifact-unverified", "backup-missing", "schema-incompatible" }, blocked.Blockers);
        Assert.Equal("release-rollback-blocked", blocked.ReasonCode);
        Assert.Equal(64, blocked.Fingerprint.Length);
        Assert.Throws<ArgumentException>(() => ReleaseRollbackEligibilityPolicy.Evaluate("rollback", "2.0.0", "v2.0.0", true, true, true));
        Assert.Throws<ArgumentException>(() => ReleaseRollbackEligibilityPolicy.Evaluate("rollback", "bad", "1.0.0", true, true, true));
    }
}

public sealed class SupportBundleCompletenessPolicyTests
{
    [Fact]
    public void Evaluate_DetectsMissingAndUnredactedSensitiveEntries()
    {
        var decision = SupportBundleCompletenessPolicy.Evaluate(new[]
        {
            new SupportBundleEntry("logs", " Logs ", true, false, false),
            new SupportBundleEntry("config", "configuration", true, true, false)
        }, new[] { "logs", "configuration", "diagnostics" });
        Assert.False(decision.Complete);
        Assert.Equal(new[] { "missing:diagnostics" }, decision.MissingCategories);
        Assert.Equal(new[] { "missing:diagnostics", "unredacted:config" }, decision.Blockers);
        Assert.Equal(67, decision.Score);
        Assert.Equal("support-bundle-incomplete", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_QualifiesCompleteBundleAndRejectsDuplicatesOrOversize()
    {
        var complete = SupportBundleCompletenessPolicy.Evaluate(new[]
        {
            new SupportBundleEntry("logs", "logs", true, false, false),
            new SupportBundleEntry("config", "configuration", true, true, true)
        }, new[] { "configuration", "logs" });
        Assert.True(complete.Complete);
        Assert.Equal(100, complete.Score);
        Assert.Equal("support-bundle-complete", complete.ReasonCode);
        Assert.Throws<ArgumentException>(() => SupportBundleCompletenessPolicy.Evaluate(new[] { new SupportBundleEntry("x", "a", true, false, false), new SupportBundleEntry("X", "b", true, false, false) }, Array.Empty<string>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => SupportBundleCompletenessPolicy.Evaluate(new[] { new SupportBundleEntry("x", "a", true, false, false), new SupportBundleEntry("y", "a", true, false, false) }, Array.Empty<string>(), 1));
    }
}
