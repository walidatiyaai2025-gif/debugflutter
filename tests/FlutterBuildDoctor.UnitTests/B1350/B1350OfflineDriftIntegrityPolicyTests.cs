using System;
using System.Linq;
using FlutterBuildDoctor.Application.Governance;
using Xunit;

namespace FlutterBuildDoctor.UnitTests.B1350;

public sealed class OfflineNetworkModePolicyTests
{
    [Fact]
    public void Evaluate_DefersNetworkWorkOfflineAndAllowsCacheOnlyWork()
    {
        var decision = OfflineNetworkModePolicy.Evaluate(" OFFLINE ", new[]
        {
            new OfflineNetworkOperation("net-b", true, false, 2),
            new OfflineNetworkOperation("cache-a", true, true, 1),
            new OfflineNetworkOperation("local-a", false, false, 0)
        });

        Assert.Equal("offline", decision.ConnectivityState);
        Assert.Equal(new[] { "cache-a", "local-a" }, decision.AllowedOperationIds);
        Assert.Equal(new[] { "net-b" }, decision.DeferredOperationIds);
        Assert.True(decision.ReconnectRequired);
        Assert.Equal("network-mode-deferred", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_OnlineAllowsAllAndBoundsDeferredLimit()
    {
        var decision = OfflineNetworkModePolicy.Evaluate("online", new[]
        {
            new OfflineNetworkOperation("b", true, false, 2),
            new OfflineNetworkOperation("a", true, false, 1)
        }, int.MaxValue);

        Assert.Equal(1000, decision.DeferredLimit);
        Assert.Equal(new[] { "a", "b" }, decision.AllowedOperationIds);
        Assert.Empty(decision.DeferredOperationIds);
        Assert.False(decision.ReconnectRequired);
        Assert.Throws<ArgumentException>(() => OfflineNetworkModePolicy.Evaluate("unknown", Array.Empty<OfflineNetworkOperation>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => OfflineNetworkModePolicy.Evaluate("offline", new[] { new OfflineNetworkOperation("a", true, false, -1) }));
    }
}

public sealed class PiiEvidenceClassificationPolicyTests
{
    [Fact]
    public void Evaluate_ClassifiesAndRedactsSupportedSensitiveEvidence()
    {
        var decision = PiiEvidenceClassificationPolicy.Evaluate(
            "ev-1",
            " diagnostic ",
            "email user@example.com phone +965 5555 1234 ip 10.1.2.3 token=abc123");

        Assert.Equal("diagnostic", decision.Category);
        Assert.Equal(new[] { "credential", "email", "ip-address", "phone" }, decision.Classifications);
        Assert.DoesNotContain("user@example.com", decision.RedactedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("abc123", decision.RedactedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[ip-address]", decision.RedactedText, StringComparison.Ordinal);
        Assert.True(decision.ContainsSensitiveData);
        Assert.Equal("pii-evidence-classified", decision.ReasonCode);
    }

    [Fact]
    public void Evaluate_BoundsRetainedTextAndKeepsClearEvidenceClear()
    {
        var clear = PiiEvidenceClassificationPolicy.Evaluate("ev-clear", "log", new string('x', 200), 10);
        Assert.Empty(clear.Classifications);
        Assert.False(clear.ContainsSensitiveData);
        Assert.Equal(32, clear.RetainedLength);
        Assert.Equal("pii-evidence-clear", clear.ReasonCode);
        Assert.Equal(64, clear.Fingerprint.Length);
    }
}

public sealed class SchemaCompatibilityPolicyTests
{
    [Fact]
    public void Evaluate_AllowsSameMajorUpgradeWithinSupportedRange()
    {
        var decision = SchemaCompatibilityPolicy.Evaluate("settings", "2.1.0", "2.4.0", "2.0.0", "2.9.9");
        Assert.True(decision.Compatible);
        Assert.Equal("upgrade", decision.ChangeKind);
        Assert.Equal("2.4.0", decision.TargetVersion);
        Assert.Equal("schema-compatible", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_RejectsDowngradeMajorChangeOutOfRangeAndInvalidBounds()
    {
        var downgrade = SchemaCompatibilityPolicy.Evaluate("settings", "2.4.0", "2.3.0", "2.0.0", "2.9.9");
        var major = SchemaCompatibilityPolicy.Evaluate("settings", "2.4.0", "3.0.0", "2.0.0", "3.9.9");
        var range = SchemaCompatibilityPolicy.Evaluate("settings", "2.4.0", "2.9.0", "2.0.0", "2.8.0");
        Assert.False(downgrade.Compatible);
        Assert.Equal("schema-downgrade-incompatible", downgrade.ReasonCode);
        Assert.Equal("schema-major-incompatible", major.ReasonCode);
        Assert.Equal("schema-version-out-of-range", range.ReasonCode);
        Assert.Throws<ArgumentException>(() => SchemaCompatibilityPolicy.Evaluate("settings", "2.0", "2.1.0", "2.0.0", "2.9.0"));
        Assert.Throws<ArgumentException>(() => SchemaCompatibilityPolicy.Evaluate("settings", "2.1.0", "2.2.0", "3.0.0", "2.0.0"));
    }
}

public sealed class ArtifactDeduplicationPolicyTests
{
    private static readonly string HashA = new('a', 64);
    private static readonly string HashB = new('b', 64);

    [Fact]
    public void Evaluate_PreservesPinnedCanonicalAndComputesReclaimedBytes()
    {
        var decision = ArtifactDeduplicationPolicy.Evaluate(new[]
        {
            new DeduplicatedArtifact("z-copy", HashA, 20, false),
            new DeduplicatedArtifact("a-pinned", HashA.ToUpperInvariant(), 30, true),
            new DeduplicatedArtifact("m-copy", HashA, 40, false),
            new DeduplicatedArtifact("unique", HashB, 50, false)
        });

        var group = Assert.Single(decision.DuplicateGroups);
        Assert.Equal("a-pinned", group.CanonicalArtifactId);
        Assert.Equal(new[] { "m-copy", "z-copy" }, group.RemovedArtifactIds);
        Assert.Equal(60, group.ReclaimedBytes);
        Assert.Equal(60, decision.TotalReclaimedBytes);
        Assert.Equal("artifact-deduplication-ready", decision.ReasonCode);
    }

    [Fact]
    public void Evaluate_HandlesUniqueArtifactsAndRejectsInvalidInput()
    {
        var decision = ArtifactDeduplicationPolicy.Evaluate(new[] { new DeduplicatedArtifact("one", HashA, 1, false) });
        Assert.Empty(decision.DuplicateGroups);
        Assert.Equal("artifact-deduplication-not-needed", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
        Assert.Throws<ArgumentException>(() => ArtifactDeduplicationPolicy.Evaluate(new[] { new DeduplicatedArtifact("bad", "abc", 1, false) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ArtifactDeduplicationPolicy.Evaluate(new[] { new DeduplicatedArtifact("bad", HashA, -1, false) }));
    }
}

public sealed class QueueStarvationPreventionPolicyTests
{
    [Fact]
    public void Evaluate_BoostsOldWorkAndOrdersDispatchDeterministically()
    {
        var now = new DateTimeOffset(2026, 8, 13, 6, 0, 0, TimeSpan.Zero);
        var decision = QueueStarvationPreventionPolicy.Evaluate(new[]
        {
            new QueueStarvationWorkItem("recent-high", now.AddMinutes(-5), 100, 0),
            new QueueStarvationWorkItem("old-low", now.AddHours(-2), 1, 2),
            new QueueStarvationWorkItem("future", now.AddMinutes(5), -10, 0)
        }, now, TimeSpan.FromMinutes(30));

        Assert.Equal("old-low", decision.DispatchOrder[0].Identity);
        Assert.True(decision.DispatchOrder[0].Starved);
        Assert.Equal(TimeSpan.Zero, decision.DispatchOrder.Single(item => item.Identity == "future").WaitAge);
        Assert.Equal(0, decision.DispatchOrder.Single(item => item.Identity == "future").BasePriority);
        Assert.Equal("queue-starvation-boosted", decision.ReasonCode);
    }

    [Fact]
    public void Evaluate_ClampsThresholdPriorityAndRejectsNegativeAttempts()
    {
        var now = DateTimeOffset.UtcNow;
        var decision = QueueStarvationPreventionPolicy.Evaluate(new[] { new QueueStarvationWorkItem("a", now, 500, 0) }, now, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromMinutes(1), decision.StarvationThreshold);
        Assert.Equal(100, decision.DispatchOrder[0].BasePriority);
        Assert.Equal(64, decision.Fingerprint.Length);
        Assert.Throws<ArgumentOutOfRangeException>(() => QueueStarvationPreventionPolicy.Evaluate(new[] { new QueueStarvationWorkItem("a", now, 1, -1) }, now, TimeSpan.FromMinutes(5)));
    }
}

public sealed class LeaseFencingTokenPolicyTests
{
    [Fact]
    public void Evaluate_RejectsStaleAndOwnerMismatchButAllowsRenewalAndNewToken()
    {
        var stale = LeaseFencingTokenPolicy.Evaluate("workspace", "owner-a", 9, 10, "owner-a");
        var mismatch = LeaseFencingTokenPolicy.Evaluate("workspace", "owner-b", 10, 10, "owner-a");
        var renewal = LeaseFencingTokenPolicy.Evaluate("workspace", "owner-a", 10, 10, "owner-a");
        var newer = LeaseFencingTokenPolicy.Evaluate("workspace", "owner-b", 11, 10, "owner-a");
        Assert.False(stale.Allowed);
        Assert.Equal("lease-fencing-stale-token", stale.ReasonCode);
        Assert.Equal("lease-fencing-owner-mismatch", mismatch.ReasonCode);
        Assert.True(renewal.Allowed);
        Assert.True(renewal.Renewal);
        Assert.True(newer.Allowed);
        Assert.False(newer.Renewal);
        Assert.Equal(64, newer.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_NormalizesIdentityAndRejectsNegativeTokens()
    {
        var decision = LeaseFencingTokenPolicy.Evaluate(" Workspace ", " OWNER-A ", 1, 0, null);
        Assert.Equal("workspace", decision.ResourceIdentity);
        Assert.Equal("owner-a", decision.LeaseOwnerIdentity);
        Assert.Throws<ArgumentOutOfRangeException>(() => LeaseFencingTokenPolicy.Evaluate("workspace", "owner-a", -1, 0, null));
    }
}

public sealed class ReleaseRingPromotionPolicyTests
{
    [Fact]
    public void Evaluate_AllowsHealthySequentialPromotionAfterSoak()
    {
        var decision = ReleaseRingPromotionPolicy.Evaluate("release-1", " CANARY ", "beta", true, TimeSpan.FromHours(8), TimeSpan.FromHours(6), false);
        Assert.True(decision.Eligible);
        Assert.Equal("canary", decision.SourceRing);
        Assert.Equal("beta", decision.TargetRing);
        Assert.Equal("release-ring-promotion-eligible", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_BlocksInvalidProgressionSkipHealthRegressionAndSoakFailures()
    {
        Assert.Equal("release-ring-invalid-progression", ReleaseRingPromotionPolicy.Evaluate("r", "beta", "canary", true, TimeSpan.FromHours(8), TimeSpan.Zero, false).ReasonCode);
        Assert.Equal("release-ring-skip-blocked", ReleaseRingPromotionPolicy.Evaluate("r", "internal", "beta", true, TimeSpan.FromHours(8), TimeSpan.Zero, false).ReasonCode);
        Assert.Equal("release-ring-source-unhealthy", ReleaseRingPromotionPolicy.Evaluate("r", "canary", "beta", false, TimeSpan.FromHours(8), TimeSpan.Zero, false).ReasonCode);
        Assert.Equal("release-ring-critical-regression", ReleaseRingPromotionPolicy.Evaluate("r", "canary", "beta", true, TimeSpan.FromHours(8), TimeSpan.Zero, true).ReasonCode);
        Assert.Equal("release-ring-soak-incomplete", ReleaseRingPromotionPolicy.Evaluate("r", "canary", "beta", true, TimeSpan.FromHours(1), TimeSpan.FromHours(2), false).ReasonCode);
        Assert.Throws<ArgumentException>(() => ReleaseRingPromotionPolicy.Evaluate("r", "unknown", "beta", true, TimeSpan.Zero, TimeSpan.Zero, false));
    }
}

public sealed class EnvironmentDriftDetectionPolicyTests
{
    [Fact]
    public void Evaluate_DetectsMissingUnexpectedChangedAndIgnoresVolatileKeys()
    {
        var decision = EnvironmentDriftDetectionPolicy.Evaluate(
            new[]
            {
                new EnvironmentSetting("sdk", " 8.0 "),
                new EnvironmentSetting("path", "expected"),
                new EnvironmentSetting("temp", "one")
            },
            new[]
            {
                new EnvironmentSetting("sdk", "9.0"),
                new EnvironmentSetting("extra", "yes"),
                new EnvironmentSetting("temp", "two")
            },
            new[] { "temp" });

        Assert.True(decision.HasDrift);
        Assert.Equal(1, decision.IgnoredVolatileKeyCount);
        Assert.Equal(new[] { "extra", "path", "sdk" }, decision.Findings.Select(finding => finding.Key));
        Assert.Equal(new[] { "unexpected", "missing", "changed" }, decision.Findings.Select(finding => finding.Kind));
        Assert.Equal("environment-drift-detected", decision.ReasonCode);
    }

    [Fact]
    public void Evaluate_IsDeterministicAndRejectsDuplicateKeys()
    {
        var first = EnvironmentDriftDetectionPolicy.Evaluate(new[] { new EnvironmentSetting("a", "x") }, new[] { new EnvironmentSetting("a", "x") });
        var second = EnvironmentDriftDetectionPolicy.Evaluate(new[] { new EnvironmentSetting("A", " x ") }, new[] { new EnvironmentSetting("a", "x") });
        Assert.False(first.HasDrift);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal("environment-drift-clear", first.ReasonCode);
        Assert.Throws<ArgumentException>(() => EnvironmentDriftDetectionPolicy.Evaluate(new[] { new EnvironmentSetting("a", "x"), new EnvironmentSetting("A", "y") }, Array.Empty<EnvironmentSetting>()));
    }
}

public sealed class CommandReplayProtectionPolicyTests
{
    [Fact]
    public void Evaluate_AcceptsUnseenCommandAndRejectsStaleOrDuplicateNonce()
    {
        var now = new DateTimeOffset(2026, 8, 13, 6, 0, 0, TimeSpan.Zero);
        var history = new[] { new CommandReplayNonce("nonce-old", now.AddMinutes(-1)) };
        var accepted = CommandReplayProtectionPolicy.Evaluate("build", "nonce-new", now.AddSeconds(-10), now, TimeSpan.FromMinutes(5), history);
        var duplicate = CommandReplayProtectionPolicy.Evaluate("build", "nonce-old", now.AddSeconds(-10), now, TimeSpan.FromMinutes(5), history);
        var stale = CommandReplayProtectionPolicy.Evaluate("build", "nonce-new", now.AddMinutes(-10), now, TimeSpan.FromMinutes(5), history);
        Assert.True(accepted.Accepted);
        Assert.Equal("command-replay-accepted", accepted.ReasonCode);
        Assert.False(duplicate.Accepted);
        Assert.Equal("command-replay-duplicate", duplicate.ReasonCode);
        Assert.False(stale.Accepted);
        Assert.Equal("command-replay-stale", stale.ReasonCode);
    }

    [Fact]
    public void Evaluate_ClampsReplayWindowAndBoundsHistoryDeterministically()
    {
        var now = DateTimeOffset.UtcNow;
        var history = Enumerable.Range(0, 20).Select(index => new CommandReplayNonce($"n-{index:D2}", now.AddSeconds(-index))).ToArray();
        var decision = CommandReplayProtectionPolicy.Evaluate("build", "fresh", now, now, TimeSpan.Zero, history, 2);
        Assert.Equal(TimeSpan.FromSeconds(1), decision.ReplayWindow);
        Assert.Equal(2, decision.RetainedHistory.Count);
        Assert.Equal(new[] { "n-00", "n-01" }, decision.RetainedHistory.Select(item => item.Nonce));
        Assert.Equal(64, decision.Fingerprint.Length);
        var future = CommandReplayProtectionPolicy.Evaluate("build", "future", now.AddMinutes(1), now, TimeSpan.FromMinutes(5), Array.Empty<CommandReplayNonce>());
        Assert.Equal("command-replay-stale", future.ReasonCode);
    }
}

public sealed class StorageConsistencyPolicyTests
{
    private static readonly string HashA = new('a', 64);
    private static readonly string HashB = new('b', 64);
    private static readonly string HashC = new('c', 64);

    [Fact]
    public void Evaluate_PreservesConsistentEntriesAndOrdersRepairCandidates()
    {
        var decision = StorageConsistencyPolicy.Evaluate(new[]
        {
            new StorageConsistencyEntry("z-missing", HashA, null),
            new StorageConsistencyEntry("a-good", HashB, HashB.ToUpperInvariant()),
            new StorageConsistencyEntry("m-bad", HashA, HashC)
        });

        Assert.False(decision.Consistent);
        Assert.Equal(new[] { "a-good" }, decision.ConsistentEntryIds);
        Assert.Equal(new[] { "m-bad", "z-missing" }, decision.RepairCandidates.Select(candidate => candidate.Identity));
        Assert.Equal("hash-mismatch", decision.RepairCandidates[0].Kind);
        Assert.Equal("missing", decision.RepairCandidates[1].Kind);
        Assert.Equal("storage-consistency-repair-required", decision.ReasonCode);
    }

    [Fact]
    public void Evaluate_ReportsCleanStorageAndRejectsInvalidHashesAndDuplicateEntries()
    {
        var clean = StorageConsistencyPolicy.Evaluate(new[] { new StorageConsistencyEntry("good", HashA, HashA) });
        Assert.True(clean.Consistent);
        Assert.Empty(clean.RepairCandidates);
        Assert.Equal("storage-consistency-valid", clean.ReasonCode);
        Assert.Equal(64, clean.Fingerprint.Length);
        Assert.Throws<ArgumentException>(() => StorageConsistencyPolicy.Evaluate(new[] { new StorageConsistencyEntry("bad", HashA, "bad") }));
        Assert.Throws<ArgumentException>(() => StorageConsistencyPolicy.Evaluate(new[] { new StorageConsistencyEntry("same", HashA, HashA), new StorageConsistencyEntry("SAME", HashB, HashB) }));
    }
}
