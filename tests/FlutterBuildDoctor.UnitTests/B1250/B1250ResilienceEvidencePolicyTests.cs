using System;
using System.Linq;
using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B1250;

public sealed class FeatureFlagSafetyPolicyTests
{
    [Fact]
    public void Evaluate_NormalizesOrdersCountsAndFingerprintsDeterministically()
    {
        var first = FeatureFlagSafetyPolicy.Evaluate(new[]
        {
            new FeatureFlagInput(" Zeta ", "STAGING", false, true, false),
            new FeatureFlagInput("alpha", "production", true, false, true)
        });
        var second = FeatureFlagSafetyPolicy.Evaluate(new[]
        {
            new FeatureFlagInput("ALPHA", "PRODUCTION", true, false, true),
            new FeatureFlagInput("zeta", "staging", false, true, false)
        });

        Assert.Equal(new[] { "alpha", "zeta" }, first.Flags.Select(flag => flag.Name));
        Assert.Equal(2, first.EnabledCount);
        Assert.Equal("feature-flags-valid", first.ReasonCode);
        Assert.Equal(64, first.Fingerprint.Length);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void Evaluate_RejectsDuplicatesInvalidEnvironmentAndUnsafeProductionOverride()
    {
        Assert.Throws<ArgumentException>(() => FeatureFlagSafetyPolicy.Evaluate(new[]
        {
            new FeatureFlagInput("flag", "test", false, false, false),
            new FeatureFlagInput("FLAG", "test", true, false, false)
        }));
        Assert.Throws<ArgumentException>(() => FeatureFlagSafetyPolicy.Evaluate(new[] { new FeatureFlagInput("flag", "qa", false, false, false) }));
        Assert.Throws<InvalidOperationException>(() => FeatureFlagSafetyPolicy.Evaluate(new[] { new FeatureFlagInput("flag", "staging", false, true, true) }));
    }
}

public sealed class DeviceReservationLeasePolicyTests
{
    [Fact]
    public void Evaluate_AllowsSameOwnerRenewalFindsConflictsAndExpiredLeases()
    {
        var now = new DateTimeOffset(2026, 8, 13, 4, 0, 0, TimeSpan.Zero);
        var decision = DeviceReservationLeasePolicy.Evaluate(new[]
        {
            new DeviceLease("device-b", "owner-x", now.AddMinutes(-5), TimeSpan.FromMinutes(20)),
            new DeviceLease("device-a", "owner-me", now.AddMinutes(-2), TimeSpan.FromMinutes(20)),
            new DeviceLease("device-c", "owner-y", now.AddHours(-2), TimeSpan.FromMinutes(5))
        }, new[] { "device-c", "device-b", "device-a" }, "OWNER-ME", now);

        Assert.Equal("device-a", decision.SelectedDeviceId);
        Assert.Equal(new[] { "device-b" }, decision.Conflicts);
        Assert.Equal(1, decision.ExpiredLeaseCount);
        Assert.Equal("device-lease-available", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_ClampsShortLeaseAndCanReportUnavailable()
    {
        var now = new DateTimeOffset(2026, 8, 13, 4, 0, 0, TimeSpan.Zero);
        var decision = DeviceReservationLeasePolicy.Evaluate(new[]
        {
            new DeviceLease("device-a", "other", now.AddSeconds(-30), TimeSpan.FromSeconds(1))
        }, new[] { "device-a" }, "me", now);

        Assert.Null(decision.SelectedDeviceId);
        Assert.Equal("device-lease-unavailable", decision.ReasonCode);
        Assert.Throws<ArgumentException>(() => DeviceReservationLeasePolicy.Evaluate(Array.Empty<DeviceLease>(), new[] { "device-a" }, "bad owner!", now));
    }
}

public sealed class NetworkBandwidthBudgetPolicyTests
{
    [Fact]
    public void Evaluate_ClampsLimitsComputesRemainingThrottleAndExhaustion()
    {
        var throttled = NetworkBandwidthBudgetPolicy.Evaluate("transfer-a", 1_500, 1_000, long.MaxValue, long.MaxValue);
        var exhausted = NetworkBandwidthBudgetPolicy.Evaluate("transfer-b", 1_100, 1_000, 100, 100);

        Assert.Equal(NetworkBandwidthBudgetPolicy.MaxBandwidthBytesPerSecond, throttled.BandwidthBytesPerSecond);
        Assert.Equal(NetworkBandwidthBudgetPolicy.MaxBurstBytes, throttled.BurstBytes);
        Assert.True(throttled.ThrottleDelay > TimeSpan.Zero);
        Assert.False(throttled.Exhausted);
        Assert.Equal("bandwidth-budget-throttled", throttled.ReasonCode);
        Assert.True(exhausted.Exhausted);
        Assert.Equal(0, exhausted.RemainingBytes);
        Assert.Equal("bandwidth-budget-exhausted", exhausted.ReasonCode);
        Assert.Equal(64, exhausted.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_RejectsNegativeMetricsAndBoundsDelay()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NetworkBandwidthBudgetPolicy.Evaluate("transfer", -1, 1, 1, 0));
        var decision = NetworkBandwidthBudgetPolicy.Evaluate("transfer", 100_000_000, 1, 1, 0);
        Assert.Equal(NetworkBandwidthBudgetPolicy.MaxThrottleDelay, decision.ThrottleDelay);
    }
}

public sealed class ArtifactRetentionWindowPolicyTests
{
    [Fact]
    public void Evaluate_PreservesPinnedAndActiveAndOrdersExpiredOldestFirst()
    {
        var now = new DateTimeOffset(2026, 8, 13, 4, 0, 0, TimeSpan.Zero);
        var decision = ArtifactRetentionWindowPolicy.Evaluate(new[]
        {
            new ArtifactRetentionItem("new", "apk", now.AddHours(-1), false, false),
            new ArtifactRetentionItem("old-b", "apk", now.AddDays(-5), false, false),
            new ArtifactRetentionItem("pinned", "logs", now.AddDays(-9), true, false),
            new ArtifactRetentionItem("old-a", "apk", now.AddDays(-7), false, false),
            new ArtifactRetentionItem("active", "logs", now.AddDays(-9), false, true)
        }, now, TimeSpan.FromDays(2));

        Assert.Equal(new[] { "old-a", "old-b" }, decision.PurgeCandidates.Select(item => item.Identity));
        Assert.Equal(3, decision.PreservedCount);
        Assert.Equal("artifact-retention-evaluated", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_ClampsRetentionAndRejectsDuplicateIdentities()
    {
        var now = DateTimeOffset.UtcNow;
        var bounded = ArtifactRetentionWindowPolicy.Evaluate(Array.Empty<ArtifactRetentionItem>(), now, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromMinutes(1), bounded.Retention);
        Assert.Throws<ArgumentException>(() => ArtifactRetentionWindowPolicy.Evaluate(new[]
        {
            new ArtifactRetentionItem("a", "apk", now, false, false),
            new ArtifactRetentionItem("A", "apk", now, false, false)
        }, now, TimeSpan.FromDays(1)));
    }
}

public sealed class CrashEvidenceNormalizationPolicyTests
{
    [Fact]
    public void Evaluate_RedactsNormalizesDeduplicatesBoundsAndSignsCrashEvidence()
    {
        var local = new DateTimeOffset(2026, 8, 13, 7, 0, 0, TimeSpan.FromHours(3));
        var decision = CrashEvidenceNormalizationPolicy.Evaluate(
            " crash-a ",
            " InvalidOperationException ",
            local,
            "token=abc password:xyz safe=value",
            new[] { @"C:\work\a.cs:10", @"C:\work\a.cs:10", @"D:\b.cs:20" },
            maxFrames: 1);

        Assert.Equal("crash-a", decision.CrashIdentity);
        Assert.Equal("invalidoperationexception", decision.ExceptionType);
        Assert.Equal(TimeSpan.Zero, decision.Timestamp.Offset);
        Assert.Contains("token=[redacted]", decision.RedactedMessage);
        Assert.Contains("password=[redacted]", decision.RedactedMessage);
        Assert.Single(decision.StackFrames);
        Assert.Contains("/", decision.StackFrames[0]);
        Assert.Equal(64, decision.Signature.Length);
        Assert.Equal(64, decision.Fingerprint.Length);
        Assert.Equal("crash-evidence-normalized", decision.ReasonCode);
    }

    [Fact]
    public void Evaluate_RequiresIdentityExceptionAndFramesCollection()
    {
        Assert.Throws<ArgumentException>(() => CrashEvidenceNormalizationPolicy.Evaluate("bad id!", "x", DateTimeOffset.UtcNow, null, Array.Empty<string>()));
        Assert.Throws<ArgumentException>(() => CrashEvidenceNormalizationPolicy.Evaluate("crash", " ", DateTimeOffset.UtcNow, null, Array.Empty<string>()));
        Assert.Throws<ArgumentNullException>(() => CrashEvidenceNormalizationPolicy.Evaluate("crash", "x", DateTimeOffset.UtcNow, null, null!));
    }
}

public sealed class DependencySourceAllowlistPolicyTests
{
    [Fact]
    public void Evaluate_NormalizesApprovesAndOrdersSourcesDeterministically()
    {
        var first = DependencySourceAllowlistPolicy.Evaluate(new[]
        {
            new DependencySource("Z", "https://PACKAGES.example.com/path/"),
            new DependencySource("a", "https://packages.example.com/core")
        }, new[] { "PACKAGES.EXAMPLE.COM" });
        var second = DependencySourceAllowlistPolicy.Evaluate(first.Sources.Reverse(), new[] { "packages.example.com" });

        Assert.Equal(new[] { "a", "z" }, first.Sources.Select(source => source.Identity));
        Assert.All(first.Sources, source => Assert.StartsWith("https://packages.example.com", source.Uri, StringComparison.Ordinal));
        Assert.Equal("dependency-sources-approved", first.ReasonCode);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void Evaluate_RejectsHttpCredentialsUnapprovedHostsAndDuplicates()
    {
        Assert.Throws<ArgumentException>(() => DependencySourceAllowlistPolicy.Evaluate(new[] { new DependencySource("a", "http://packages.example.com") }, new[] { "packages.example.com" }));
        Assert.Throws<ArgumentException>(() => DependencySourceAllowlistPolicy.Evaluate(new[] { new DependencySource("a", "https://user:pass@packages.example.com") }, new[] { "packages.example.com" }));
        Assert.Throws<InvalidOperationException>(() => DependencySourceAllowlistPolicy.Evaluate(new[] { new DependencySource("a", "https://evil.example.com") }, new[] { "packages.example.com" }));
        Assert.Throws<ArgumentException>(() => DependencySourceAllowlistPolicy.Evaluate(new[]
        {
            new DependencySource("a", "https://packages.example.com/one"),
            new DependencySource("A", "https://packages.example.com/two")
        }, new[] { "packages.example.com" }));
    }
}

public sealed class ProcessTreeOwnershipPolicyTests
{
    [Fact]
    public void Evaluate_ComputesRootAndRestrictsTerminationToOwnedDescendants()
    {
        var decision = ProcessTreeOwnershipPolicy.Evaluate(new[]
        {
            new ProcessTreeNode(10, null, true),
            new ProcessTreeNode(11, 10, true),
            new ProcessTreeNode(12, 11, false),
            new ProcessTreeNode(13, 11, true),
            new ProcessTreeNode(20, null, true)
        }, 11);

        Assert.Equal(10, decision.RootProcessId);
        Assert.Equal(new[] { 11, 13 }, decision.TerminableProcessIds);
        Assert.Equal("process-tree-ownership-valid", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_RejectsInvalidDuplicateSelfParentUnknownParentAndCycles()
    {
        Assert.Throws<ArgumentException>(() => ProcessTreeOwnershipPolicy.Evaluate(new[] { new ProcessTreeNode(0, null, true) }, 1));
        Assert.Throws<ArgumentException>(() => ProcessTreeOwnershipPolicy.Evaluate(new[] { new ProcessTreeNode(1, null, true), new ProcessTreeNode(1, null, true) }, 1));
        Assert.Throws<ArgumentException>(() => ProcessTreeOwnershipPolicy.Evaluate(new[] { new ProcessTreeNode(1, 1, true) }, 1));
        Assert.Throws<ArgumentException>(() => ProcessTreeOwnershipPolicy.Evaluate(new[] { new ProcessTreeNode(1, 2, true) }, 1));
        Assert.Throws<ArgumentException>(() => ProcessTreeOwnershipPolicy.Evaluate(new[] { new ProcessTreeNode(1, 2, true), new ProcessTreeNode(2, 1, true) }, 1));
    }
}

public sealed class NestedRetryBudgetPolicyTests
{
    [Fact]
    public void Evaluate_ClampsOrdersAllocatesAndFingerprintsDeterministically()
    {
        var first = NestedRetryBudgetPolicy.Evaluate("root", 200, 10, new[]
        {
            new ChildRetryScope("z", 20, 1),
            new ChildRetryScope("a", 30, 2)
        });
        var second = NestedRetryBudgetPolicy.Evaluate("ROOT", 100, 10, new[]
        {
            new ChildRetryScope("A", 30, 2),
            new ChildRetryScope("Z", 20, 1)
        });

        Assert.Equal(100, first.TotalAttempts);
        Assert.Equal(40, first.RemainingAttempts);
        Assert.Equal(new[] { "a", "z" }, first.Children.Select(child => child.Identity));
        Assert.Equal("nested-retry-budget-available", first.ReasonCode);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void Evaluate_DetectsExhaustionAndRejectsInvalidAllocations()
    {
        var exhausted = NestedRetryBudgetPolicy.Evaluate("root", 3, 3, Array.Empty<ChildRetryScope>());
        Assert.Equal("nested-retry-budget-exhausted", exhausted.ReasonCode);
        Assert.Throws<ArgumentOutOfRangeException>(() => NestedRetryBudgetPolicy.Evaluate("root", -1, 0, Array.Empty<ChildRetryScope>()));
        Assert.Throws<ArgumentException>(() => NestedRetryBudgetPolicy.Evaluate("root", 5, 6, Array.Empty<ChildRetryScope>()));
        Assert.Throws<InvalidOperationException>(() => NestedRetryBudgetPolicy.Evaluate("root", 5, 1, new[] { new ChildRetryScope("child", 5, 0) }));
        Assert.Throws<ArgumentException>(() => NestedRetryBudgetPolicy.Evaluate("root", 10, 0, new[] { new ChildRetryScope("a", 1, 0), new ChildRetryScope("A", 1, 0) }));
    }
}

public sealed class TestFlakeClassificationPolicyTests
{
    [Fact]
    public void Evaluate_ClassifiesStableIntermittentFailingAndMandatoryWithDeterministicOrder()
    {
        var decision = TestFlakeClassificationPolicy.Evaluate(new[]
        {
            new TestFlakeObservation("stable", "suite", 10, 10, 0, false),
            new TestFlakeObservation("flaky", "suite", 10, 8, 2, false),
            new TestFlakeObservation("bad", "suite", 10, 4, 6, false),
            new TestFlakeObservation("critical", "suite", 10, 9, 1, true)
        });

        Assert.Equal(new[] { "critical", "bad", "flaky", "stable" }, decision.Tests.Select(test => test.TestIdentity));
        Assert.Equal("failing-mandatory", decision.Tests[0].Classification);
        Assert.Equal("failing", decision.Tests[1].Classification);
        Assert.Equal("intermittent", decision.Tests[2].Classification);
        Assert.Equal("stable", decision.Tests[3].Classification);
        Assert.Equal(1, decision.IntermittentCount);
        Assert.Equal(2, decision.FailingCount);
        Assert.Equal("test-flake-classified", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_RejectsNegativeInvalidArithmeticAndDuplicateObservations()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TestFlakeClassificationPolicy.Evaluate(new[] { new TestFlakeObservation("t", "s", -1, 0, 0, false) }));
        Assert.Throws<ArgumentException>(() => TestFlakeClassificationPolicy.Evaluate(new[] { new TestFlakeObservation("t", "s", 2, 2, 1, false) }));
        Assert.Throws<ArgumentException>(() => TestFlakeClassificationPolicy.Evaluate(new[]
        {
            new TestFlakeObservation("t", "s", 1, 1, 0, false),
            new TestFlakeObservation("T", "S", 1, 1, 0, false)
        }));
    }
}

public sealed class ReleaseEvidenceCompletenessPolicyTests
{
    [Fact]
    public void Evaluate_RequiresCategoriesRejectsFailedMandatoryAndComputesScore()
    {
        var decision = ReleaseEvidenceCompletenessPolicy.Evaluate(new[]
        {
            new ReleaseEvidenceEntry("build", "build", true, true),
            new ReleaseEvidenceEntry("tests", "tests", false, true)
        }, new[] { "build", "tests", "signing" });

        Assert.False(decision.Complete);
        Assert.Equal(new[] { "failed:tests", "missing:signing" }, decision.Blockers);
        Assert.Equal(33, decision.Score);
        Assert.Equal("release-evidence-incomplete", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_CompletesWhenRequiredEvidencePassesAndRejectsDuplicatesOrOversizeSets()
    {
        var complete = ReleaseEvidenceCompletenessPolicy.Evaluate(new[]
        {
            new ReleaseEvidenceEntry("tests", "tests", true, true),
            new ReleaseEvidenceEntry("build", "build", true, true)
        }, new[] { "tests", "build" });
        Assert.True(complete.Complete);
        Assert.Equal(100, complete.Score);
        Assert.Equal("release-evidence-complete", complete.ReasonCode);

        Assert.Throws<ArgumentException>(() => ReleaseEvidenceCompletenessPolicy.Evaluate(new[]
        {
            new ReleaseEvidenceEntry("a", "build", true, true),
            new ReleaseEvidenceEntry("A", "tests", true, true)
        }, new[] { "build" }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReleaseEvidenceCompletenessPolicy.Evaluate(new[]
        {
            new ReleaseEvidenceEntry("a", "build", true, true),
            new ReleaseEvidenceEntry("b", "tests", true, true)
        }, new[] { "build" }, maxEntries: 1));
    }
}
