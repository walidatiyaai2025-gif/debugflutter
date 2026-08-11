using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B1050;

public sealed class ToolchainVersionCompatibilityPolicyTests
{
    [Fact]
    public void Evaluate_NormalizesOrdersAndPassesCompatibleSet()
    {
        var first = ToolchainVersionCompatibilityPolicy.Evaluate(new[]
        {
            new ToolchainComponentVersion(" Flutter ", "v3.22.1", "3.10.0", 4),
            new ToolchainComponentVersion("ANDROID-SDK", "35.0.0", "34.0.0", 35)
        });
        var second = ToolchainVersionCompatibilityPolicy.Evaluate(first.Components.AsEnumerable().Reverse());
        Assert.True(first.Compatible);
        Assert.Equal(new[] { "android-sdk", "flutter" }, first.Components.Select(x => x.Identity));
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal("toolchain-compatible", first.ReasonCode);
    }

    [Fact]
    public void Evaluate_SurfacesVersionFailures()
    {
        var decision = ToolchainVersionCompatibilityPolicy.Evaluate(new[]
        {
            new ToolchainComponentVersion("flutter", "3.5.0", "3.10.0", 3),
            new ToolchainComponentVersion("java", "21.0.0", "17.0.0", 20),
            new ToolchainComponentVersion("gradle", "8.1.0", "9.0.0", 8)
        });
        Assert.False(decision.Compatible);
        Assert.Contains(decision.Failures, x => x.Reason == "below-minimum");
        Assert.Contains(decision.Failures, x => x.Reason == "above-maximum-major");
        Assert.Contains(decision.Failures, x => x.Reason == "incompatible-supported-range");
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_RejectsDuplicatesInvalidVersionsAndBoundsCount()
    {
        Assert.Throws<ArgumentException>(() => ToolchainVersionCompatibilityPolicy.Evaluate(new[] { new ToolchainComponentVersion("flutter", "3.0.0", "3.0.0", 4), new ToolchainComponentVersion("FLUTTER", "3.1.0", "3.0.0", 4) }));
        Assert.Throws<ArgumentException>(() => ToolchainVersionCompatibilityPolicy.Evaluate(new[] { new ToolchainComponentVersion("flutter", "stable", "3.0.0", 4) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ToolchainVersionCompatibilityPolicy.Evaluate(new[] { new ToolchainComponentVersion("a", "1.0.0", "1.0.0", 1), new ToolchainComponentVersion("b", "1.0.0", "1.0.0", 1) }, 1));
    }
}

public sealed class SdkLicenseReadinessPolicyTests
{
    [Fact]
    public void Evaluate_TracksInstallLicenseMandatoryAndReadiness()
    {
        var ready = SdkLicenseReadinessPolicy.Evaluate(new[] { new SdkPackageState(" Platform-Tools ", true, true, true), new SdkPackageState("build-tools;35.0.0", true, true, false) }, new[] { "platform-tools" });
        Assert.True(ready.Ready);
        Assert.Equal(100, ready.Score);
        Assert.Empty(ready.Blockers);
        Assert.Equal("platform-tools", ready.Packages[1].Identity);
        Assert.Equal("sdk-readiness-ready", ready.ReasonCode);
    }

    [Fact]
    public void Evaluate_SurfacesMissingAndUnlicensedBlockers()
    {
        var decision = SdkLicenseReadinessPolicy.Evaluate(new[] { new SdkPackageState("platform-tools", false, false, true), new SdkPackageState("cmdline-tools", true, false, true) }, new[] { "emulator" });
        Assert.False(decision.Ready);
        Assert.Contains("missing:emulator", decision.Blockers);
        Assert.Contains("missing:platform-tools", decision.Blockers);
        Assert.Contains("unlicensed:cmdline-tools", decision.Blockers);
        Assert.InRange(decision.Score, 0, 99);
    }

    [Fact]
    public void Evaluate_RejectsDuplicatesAndBoundsPackageCount()
    {
        Assert.Throws<ArgumentException>(() => SdkLicenseReadinessPolicy.Evaluate(new[] { new SdkPackageState("a", true, true, false), new SdkPackageState("A", true, true, false) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => SdkLicenseReadinessPolicy.Evaluate(new[] { new SdkPackageState("a", true, true, false), new SdkPackageState("b", true, true, false) }, maxPackages: 1));
    }
}

public sealed class EmulatorBootHealthPolicyTests
{
    [Fact]
    public void Evaluate_NormalizesUtcClampsTimeoutAndClassifiesStates()
    {
        var local = new DateTimeOffset(2026, 8, 12, 3, 0, 0, TimeSpan.FromHours(3));
        var healthy = EmulatorBootHealthPolicy.Evaluate(" Pixel_8 ", local, TimeSpan.FromSeconds(20), TimeSpan.Zero, true, true);
        Assert.Equal("pixel_8", healthy.EmulatorIdentity);
        Assert.Equal(TimeSpan.Zero, healthy.ObservedAtUtc.Offset);
        Assert.Equal(EmulatorBootHealthPolicy.MinTimeout, healthy.BootTimeout);
        Assert.Equal(EmulatorHealthStatus.Healthy, healthy.Status);
        Assert.Equal(EmulatorHealthStatus.Offline, EmulatorBootHealthPolicy.Evaluate("emu", local, TimeSpan.Zero, TimeSpan.FromMinutes(1), false, false).Status);
        Assert.Equal(EmulatorHealthStatus.Booting, EmulatorBootHealthPolicy.Evaluate("emu", local, TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(1), true, false).Status);
        Assert.Equal(EmulatorHealthStatus.TimedOut, EmulatorBootHealthPolicy.Evaluate("emu", local, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(1), true, false).Status);
    }

    [Fact]
    public void Evaluate_RejectsNegativeDurationAndProducesFingerprint()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EmulatorBootHealthPolicy.Evaluate("emu", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(-1), TimeSpan.FromMinutes(1), true, false));
        Assert.Equal(64, EmulatorBootHealthPolicy.Evaluate("emu", DateTimeOffset.UtcNow, TimeSpan.Zero, TimeSpan.FromMinutes(1), true, false).Fingerprint.Length);
    }
}

public sealed class ProcessHeartbeatPolicyTests
{
    [Fact]
    public void Evaluate_DetectsResponsiveStalledAndCompletedStates()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(ProcessHeartbeatStatus.Responsive, ProcessHeartbeatPolicy.Evaluate("build", now, now.AddSeconds(-10), TimeSpan.FromSeconds(30), false).Status);
        var stalled = ProcessHeartbeatPolicy.Evaluate("build", now, now.AddMinutes(-2), TimeSpan.FromSeconds(30), false);
        Assert.Equal(ProcessHeartbeatStatus.Stalled, stalled.Status);
        Assert.True(stalled.StallAge > stalled.HeartbeatTimeout);
        Assert.Equal(ProcessHeartbeatStatus.Completed, ProcessHeartbeatPolicy.Evaluate("build", now, now.AddMinutes(-2), TimeSpan.FromSeconds(30), true).Status);
    }

    [Fact]
    public void Evaluate_RejectsFutureHeartbeatAndClampsTimeout()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentOutOfRangeException>(() => ProcessHeartbeatPolicy.Evaluate("build", now, now.AddSeconds(30), TimeSpan.FromSeconds(30), false));
        var clamped = ProcessHeartbeatPolicy.Evaluate("build", now, now, TimeSpan.Zero, false);
        Assert.Equal(ProcessHeartbeatPolicy.MinTimeout, clamped.HeartbeatTimeout);
        Assert.Equal(TimeSpan.Zero, clamped.ObservedAtUtc.Offset);
        Assert.Equal(64, clamped.Fingerprint.Length);
    }
}

public sealed class LogChunkIntegrityPolicyTests
{
    private const string HashA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string HashB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void Evaluate_NormalizesOrdersAndFingerprintsChunks()
    {
        var first = LogChunkIntegrityPolicy.Evaluate(" Build-Log ", new[] { new LogChunk(2, HashB.ToUpperInvariant(), 20), new LogChunk(1, HashA, 10) }, true);
        var second = LogChunkIntegrityPolicy.Evaluate("build-log", first.Chunks.AsEnumerable().Reverse(), true);
        Assert.Equal(new[] { 1, 2 }, first.Chunks.Select(x => x.Sequence));
        Assert.Equal(HashB, first.Chunks[1].Sha256);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal("log-chunk-integrity-valid", first.ReasonCode);
    }

    [Fact]
    public void Evaluate_RejectsDuplicatesGapsBadHashesNegativeLengthsAndBounds()
    {
        Assert.Throws<ArgumentException>(() => LogChunkIntegrityPolicy.Evaluate("log", new[] { new LogChunk(1, HashA, 1), new LogChunk(1, HashB, 1) }, false));
        Assert.Throws<ArgumentException>(() => LogChunkIntegrityPolicy.Evaluate("log", new[] { new LogChunk(1, HashA, 1), new LogChunk(3, HashB, 1) }, true));
        Assert.Throws<ArgumentException>(() => LogChunkIntegrityPolicy.Evaluate("log", new[] { new LogChunk(1, "bad", 1) }, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => LogChunkIntegrityPolicy.Evaluate("log", new[] { new LogChunk(1, HashA, -1) }, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => LogChunkIntegrityPolicy.Evaluate("log", new[] { new LogChunk(1, HashA, 1), new LogChunk(2, HashB, 1) }, false, 1));
    }
}

public sealed class BackupRotationPolicyTests
{
    [Fact]
    public void Evaluate_PreservesPinnedNewestAndEvictsOldestByCountOrBytes()
    {
        var now = DateTimeOffset.UtcNow;
        var decision = BackupRotationPolicy.Evaluate(new[] { new BackupEntry("old", now.AddDays(-3), 80, false), new BackupEntry("pinned", now.AddDays(-4), 10, true), new BackupEntry("new", now, 80, false) }, 2, 100, 1);
        Assert.Contains(decision.Retained, x => x.Identity == "pinned");
        Assert.Contains(decision.Retained, x => x.Identity == "new");
        Assert.Equal("old", Assert.Single(decision.Evicted).Identity);
        Assert.Equal(90, decision.RetainedBytes);
        Assert.Equal("backup-rotation-evicted", decision.ReasonCode);
    }

    [Fact]
    public void Evaluate_RejectsNegativeSizeAndReportsProtectedOverLimit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BackupRotationPolicy.Evaluate(new[] { new BackupEntry("a", DateTimeOffset.UtcNow, -1, false) }, 1, 1));
        var protectedOnly = BackupRotationPolicy.Evaluate(new[] { new BackupEntry("a", DateTimeOffset.UtcNow, 100, true) }, 1, 10, 1);
        Assert.Equal("backup-rotation-protected-over-limit", protectedOnly.ReasonCode);
        Assert.Equal(64, protectedOnly.Fingerprint.Length);
    }
}

public sealed class CleanupEligibilityPolicyTests
{
    [Fact]
    public void Evaluate_PreservesProtectedActiveFreshAndRanksEligible()
    {
        var now = DateTimeOffset.UtcNow;
        var decision = CleanupEligibilityPolicy.Evaluate(new[]
        {
            new CleanupCandidate("old-small", now.AddDays(-5), 10, false, false),
            new CleanupCandidate("old-large", now.AddDays(-5), 20, false, false),
            new CleanupCandidate("protected", now.AddDays(-20), 50, true, false),
            new CleanupCandidate("active", now.AddDays(-20), 60, false, true),
            new CleanupCandidate("fresh", now.AddMinutes(-1), 70, false, false)
        }, now, TimeSpan.FromDays(1));
        Assert.Equal(new[] { "old-large", "old-small" }, decision.Eligible.Select(x => x.Identity));
        Assert.Equal(30, decision.ReclaimableBytes);
        Assert.Equal("cleanup-eligible", decision.ReasonCode);
        Assert.Contains(decision.Preserved, x => x.Identity == "protected");
        Assert.Contains(decision.Preserved, x => x.Identity == "active");
    }

    [Fact]
    public void Evaluate_RejectsNegativeSizesDuplicatesAndReportsNoneEligible()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentOutOfRangeException>(() => CleanupEligibilityPolicy.Evaluate(new[] { new CleanupCandidate("a", now, -1, false, false) }, now, TimeSpan.FromDays(1)));
        Assert.Throws<ArgumentException>(() => CleanupEligibilityPolicy.Evaluate(new[] { new CleanupCandidate("a", now, 1, false, false), new CleanupCandidate("A", now, 1, false, false) }, now, TimeSpan.FromDays(1)));
        var none = CleanupEligibilityPolicy.Evaluate(new[] { new CleanupCandidate("fresh", now, 1, false, false) }, now, TimeSpan.FromDays(1));
        Assert.Empty(none.Eligible);
        Assert.Equal("cleanup-none-eligible", none.ReasonCode);
        Assert.Equal(64, none.Fingerprint.Length);
    }
}

public sealed class EndpointHealthScoringPolicyTests
{
    [Fact]
    public void Evaluate_ClampsScoresNormalizesUtcAndRanksEndpoints()
    {
        var local = new DateTimeOffset(2026, 8, 12, 3, 0, 0, TimeSpan.FromHours(3));
        var first = EndpointHealthScoringPolicy.Evaluate(new[] { new EndpointObservation(" slow ", local, TimeSpan.FromSeconds(2), 110), new EndpointObservation("fast", local, TimeSpan.FromMilliseconds(50), 90) });
        var second = EndpointHealthScoringPolicy.Evaluate(new[] { new EndpointObservation("FAST", local, TimeSpan.FromMilliseconds(50), 90), new EndpointObservation("SLOW", local, TimeSpan.FromSeconds(2), 110) });
        Assert.Equal("fast", first.Endpoints[0].Identity);
        Assert.Equal(100, first.Endpoints.Single(x => x.Identity == "slow").SuccessRatePercent);
        Assert.All(first.Endpoints, x => Assert.InRange(x.HealthScore, 0, 100));
        Assert.Equal(TimeSpan.Zero, first.Endpoints[0].ObservedAtUtc.Offset);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void Evaluate_RejectsNegativeLatencyDuplicatesAndHandlesEmptySet()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentOutOfRangeException>(() => EndpointHealthScoringPolicy.Evaluate(new[] { new EndpointObservation("api", now, TimeSpan.FromMilliseconds(-1), 100) }));
        Assert.Throws<ArgumentException>(() => EndpointHealthScoringPolicy.Evaluate(new[] { new EndpointObservation("api", now, TimeSpan.Zero, 100), new EndpointObservation("API", now, TimeSpan.Zero, 100) }));
        Assert.Equal("endpoint-health-empty", EndpointHealthScoringPolicy.Evaluate(Array.Empty<EndpointObservation>()).ReasonCode);
    }
}

public sealed class BuildTargetCompatibilityMatrixPolicyTests
{
    [Fact]
    public void Evaluate_NormalizesCapabilitiesCountsSupportAndIsDeterministic()
    {
        var first = BuildTargetCompatibilityMatrixPolicy.Evaluate(new[]
        {
            new BuildTargetCompatibilityEntry(" AppBundle ", "Android", "Release", new[] { "flutter", "java", "android-sdk" }),
            new BuildTargetCompatibilityEntry("apk", "android", "debug", new[] { "flutter" })
        }, new[] { "FLUTTER", "JAVA", "ANDROID-SDK" });
        var second = BuildTargetCompatibilityMatrixPolicy.Evaluate(first.Results.Select(x => x.Target).AsEnumerable().Reverse(), new[] { "android-sdk", "java", "flutter" });
        Assert.Equal(2, first.SupportedTargetCount);
        Assert.Empty(first.Blockers);
        Assert.All(first.Results, x => Assert.True(x.Supported));
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal("build-target-matrix-supported", first.ReasonCode);
    }

    [Fact]
    public void Evaluate_SurfacesMissingCapabilitiesAndRejectsDuplicates()
    {
        var blocked = BuildTargetCompatibilityMatrixPolicy.Evaluate(new[] { new BuildTargetCompatibilityEntry("bundle", "android", "release", new[] { "flutter", "signing" }) }, new[] { "flutter" });
        Assert.Equal(0, blocked.SupportedTargetCount);
        Assert.Equal(new[] { "signing" }, blocked.Results[0].MissingCapabilities);
        Assert.Contains("unsupported:bundle:signing", blocked.Blockers);
        Assert.Throws<ArgumentException>(() => BuildTargetCompatibilityMatrixPolicy.Evaluate(new[] { new BuildTargetCompatibilityEntry("bundle", "android", "release", Array.Empty<string>()), new BuildTargetCompatibilityEntry("BUNDLE", "ANDROID", "RELEASE", Array.Empty<string>()) }, Array.Empty<string>()));
    }
}

public sealed class ReleaseCandidateQualificationPolicyTests
{
    [Fact]
    public void Evaluate_QualifiesOnlyWithZeroMandatoryBlockersAndIsDeterministic()
    {
        var first = ReleaseCandidateQualificationPolicy.Evaluate(" RC-1 ", new[] { new ReleaseQualificationCheck("tests", "quality", true, true, 5), new ReleaseQualificationCheck("build", "quality", true, true, 5), new ReleaseQualificationCheck("telemetry", "optional", false, false, 1) }, new[] { "build", "tests" });
        var second = ReleaseCandidateQualificationPolicy.Evaluate("rc-1", first.Checks.AsEnumerable().Reverse(), new[] { "TESTS", "BUILD" });
        Assert.True(first.Qualified);
        Assert.Empty(first.MandatoryBlockers);
        Assert.InRange(first.Score, 90, 100);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal("release-candidate-qualified", first.ReasonCode);
    }

    [Fact]
    public void Evaluate_SurfacesFailedMissingBlockersAndComputesScore()
    {
        var decision = ReleaseCandidateQualificationPolicy.Evaluate("rc-2", new[] { new ReleaseQualificationCheck("build", "quality", true, false, 5), new ReleaseQualificationCheck("tests", "quality", false, true, 5) }, new[] { "build", "tests", "signing" });
        Assert.False(decision.Qualified);
        Assert.Contains("failed:build", decision.MandatoryBlockers);
        Assert.Contains("missing:signing", decision.MandatoryBlockers);
        Assert.Equal(50, decision.Score);
        Assert.Equal("release-candidate-blocked", decision.ReasonCode);
    }

    [Fact]
    public void Evaluate_RejectsDuplicatesBoundsCheckCountAndClampsWeight()
    {
        Assert.Throws<ArgumentException>(() => ReleaseCandidateQualificationPolicy.Evaluate("rc", new[] { new ReleaseQualificationCheck("build", "quality", true, true), new ReleaseQualificationCheck("BUILD", "quality", true, true) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReleaseCandidateQualificationPolicy.Evaluate("rc", new[] { new ReleaseQualificationCheck("build", "quality", true, true), new ReleaseQualificationCheck("tests", "quality", true, true) }, maxChecks: 1));
        var clamped = ReleaseCandidateQualificationPolicy.Evaluate("rc", new[] { new ReleaseQualificationCheck("build", "quality", false, true, 500) });
        Assert.Equal(100, clamped.Checks[0].Weight);
        Assert.Equal(100, clamped.Score);
        Assert.Equal(64, clamped.Fingerprint.Length);
    }
}
