using System;
using System.Linq;
using FlutterBuildDoctor.Application.Governance;
using Xunit;

namespace FlutterBuildDoctor.UnitTests.B1450;

public sealed class ClockSkewConfidencePolicyTests
{
    [Fact]
    public void Evaluate_NormalizesOrdersClampsAndClassifiesClockConfidence()
    {
        var later = new DateTimeOffset(2026, 8, 13, 12, 1, 0, TimeSpan.FromHours(3));
        var earlier = later.AddMinutes(-1);
        var decision = ClockSkewConfidencePolicy.Evaluate(new[]
        {
            new ClockSkewSample("SAMPLE-B", later, later.AddSeconds(3), later, TimeSpan.Zero),
            new ClockSkewSample("sample-a", earlier, earlier.AddMilliseconds(500), earlier, TimeSpan.Zero)
        }, TimeSpan.Zero);

        Assert.Equal(ClockSkewConfidencePolicy.MinAllowableSkew, decision.AllowableSkew);
        Assert.Equal(new[] { "sample-a", "sample-b" }, decision.Samples.Select(sample => sample.Identity).ToArray());
        Assert.All(decision.Samples, sample => Assert.Equal(TimeSpan.Zero, sample.ObservedAtUtc.Offset));
        Assert.Equal("untrusted", decision.Confidence);
        Assert.True(decision.ResynchronizationRequired);
        Assert.Equal("clock-confidence-untrusted", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_AccountsForUncertaintyAndWarnsAtIntermediateSkew()
    {
        var now = DateTimeOffset.UtcNow;
        var decision = ClockSkewConfidencePolicy.Evaluate(new[]
        {
            new ClockSkewSample("sample", now, now.AddSeconds(2.5), now, TimeSpan.FromSeconds(1))
        }, TimeSpan.FromSeconds(1));

        Assert.Equal(TimeSpan.FromSeconds(1.5), decision.WorstEffectiveSkew);
        Assert.Equal("warn", decision.Confidence);
        Assert.True(decision.ResynchronizationRequired);
    }

    [Fact]
    public void Evaluate_RejectsNegativeUncertaintyAndDuplicateIdentityAndClampsMaximum()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentOutOfRangeException>(() => ClockSkewConfidencePolicy.Evaluate(new[]
        {
            new ClockSkewSample("sample", now, now, now, TimeSpan.FromSeconds(-1))
        }, TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentException>(() => ClockSkewConfidencePolicy.Evaluate(new[]
        {
            new ClockSkewSample("sample", now, now, now, TimeSpan.Zero),
            new ClockSkewSample("SAMPLE", now.AddSeconds(1), now, now, TimeSpan.Zero)
        }, TimeSpan.FromSeconds(1)));
        var bounded = ClockSkewConfidencePolicy.Evaluate(new[] { new ClockSkewSample("sample", now, now, now, TimeSpan.Zero) }, TimeSpan.FromDays(1));
        Assert.Equal(ClockSkewConfidencePolicy.MaxAllowableSkew, bounded.AllowableSkew);
        Assert.Equal("trusted", bounded.Confidence);
        Assert.False(bounded.ResynchronizationRequired);
    }
}

public sealed class DnsResolutionSafetyPolicyTests
{
    [Fact]
    public void Evaluate_NormalizesDeduplicatesPrefersPrivateAndDetectsStableResolution()
    {
        var decision = DnsResolutionSafetyPolicy.Evaluate("API-ENDPOINT", "Example.COM.", new[] { "8.8.8.8", "10.0.0.1", "10.0.0.1" }, new[] { "10.0.0.1", "8.8.8.8" });

        Assert.Equal("api-endpoint", decision.EndpointIdentity);
        Assert.Equal("example.com", decision.Hostname);
        Assert.Equal(new[] { "10.0.0.1", "8.8.8.8" }, decision.ResolvedAddresses);
        Assert.Equal("private", decision.PreferredAddressClass);
        Assert.False(decision.ResolutionDrifted);
        Assert.Equal("dns-resolution-stable", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_DetectsResolutionDriftAndPublicPreference()
    {
        var decision = DnsResolutionSafetyPolicy.Evaluate("api", "api.example.com", new[] { "8.8.4.4" }, new[] { "1.1.1.1" });
        Assert.True(decision.ResolutionDrifted);
        Assert.Equal("public", decision.PreferredAddressClass);
        Assert.Equal("dns-resolution-drifted", decision.ReasonCode);
    }

    [Fact]
    public void Evaluate_RejectsInvalidHostnameEmptyResolutionAndInvalidAddress()
    {
        Assert.Throws<ArgumentException>(() => DnsResolutionSafetyPolicy.Evaluate("api", "bad host", new[] { "8.8.8.8" }));
        Assert.Throws<ArgumentException>(() => DnsResolutionSafetyPolicy.Evaluate("api", "example.com", Array.Empty<string>()));
        Assert.Throws<ArgumentException>(() => DnsResolutionSafetyPolicy.Evaluate("api", "example.com", new[] { "not-an-ip" }));
    }
}

public sealed class PackageVulnerabilityEvidencePolicyTests
{
    [Fact]
    public void Evaluate_NormalizesDeduplicatesClampsSeverityAndBlocksCriticalPackage()
    {
        var decision = PackageVulnerabilityEvidencePolicy.Evaluate("PACKAGE-A", "1.2.3", new[]
        {
            new PackageVulnerabilityAdvisory("ADV-1", 5.0, false),
            new PackageVulnerabilityAdvisory("adv-1", 9.5, true),
            new PackageVulnerabilityAdvisory("adv-2", 12.0, false)
        });

        Assert.Equal("package-a", decision.PackageIdentity);
        Assert.Equal(2, decision.Advisories.Count);
        Assert.Equal(10d, decision.HighestSeverity);
        Assert.Equal("adv-2", decision.Advisories[0].AdvisoryIdentity);
        Assert.True(decision.Blocked);
        Assert.Equal("package-vulnerability-blocked", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_ClassifiesClearAndObservedNonCriticalPackages()
    {
        var clear = PackageVulnerabilityEvidencePolicy.Evaluate("package", "1.0.0", Array.Empty<PackageVulnerabilityAdvisory>());
        var observed = PackageVulnerabilityEvidencePolicy.Evaluate("package", "1.0.0-beta.1", new[] { new PackageVulnerabilityAdvisory("adv", 4.0, false) });
        Assert.False(clear.Blocked);
        Assert.Equal("package-vulnerability-clear", clear.ReasonCode);
        Assert.False(observed.Blocked);
        Assert.Equal("package-vulnerability-observed", observed.ReasonCode);
    }

    [Fact]
    public void Evaluate_RejectsInvalidVersionAndNegativeSeverity()
    {
        Assert.Throws<ArgumentException>(() => PackageVulnerabilityEvidencePolicy.Evaluate("package", "v1", Array.Empty<PackageVulnerabilityAdvisory>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => PackageVulnerabilityEvidencePolicy.Evaluate("package", "1.0.0", new[] { new PackageVulnerabilityAdvisory("adv", -0.1, false) }));
    }
}

public sealed class FilesystemCasePortabilityPolicyTests
{
    [Fact]
    public void Evaluate_NormalizesSeparatorsDetectsCaseCollisionsAndReservedNames()
    {
        var decision = FilesystemCasePortabilityPolicy.Evaluate(new[] { "Src/File.cs", "src/file.cs", "CON.txt", "lib\\ok.cs" });

        Assert.False(decision.Portable);
        Assert.Contains(decision.CanonicalPaths, path => path == "lib/ok.cs");
        Assert.Equal(2, decision.Findings.Count(finding => finding.FindingType == "case-collision"));
        Assert.Single(decision.Findings.Where(finding => finding.FindingType == "reserved-name"));
        Assert.Equal("filesystem-portability-conflict", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_PreservesCanonicalSpellingAndAcceptsPortablePaths()
    {
        var decision = FilesystemCasePortabilityPolicy.Evaluate(new[] { "src/Main.cs", "assets/icon.png", "src/Main.cs" });
        Assert.True(decision.Portable);
        Assert.Equal(new[] { "assets/icon.png", "src/Main.cs" }, decision.CanonicalPaths);
        Assert.Empty(decision.Findings);
        Assert.Equal("filesystem-portable", decision.ReasonCode);
    }

    [Fact]
    public void Evaluate_RejectsRootedAndTraversalPaths()
    {
        Assert.Throws<ArgumentException>(() => FilesystemCasePortabilityPolicy.Evaluate(new[] { "/root/file" }));
        Assert.Throws<ArgumentException>(() => FilesystemCasePortabilityPolicy.Evaluate(new[] { "C:\\temp\\file" }));
        Assert.Throws<ArgumentException>(() => FilesystemCasePortabilityPolicy.Evaluate(new[] { "src/../secret" }));
    }
}

public sealed class SubprocessExitCodeNormalizationPolicyTests
{
    [Fact]
    public void Evaluate_NormalizesPlatformAndRecognizesSuccessCancellationAndTimeout()
    {
        var success = SubprocessExitCodeNormalizationPolicy.Evaluate("BUILD", 0, "WINDOWS");
        var cancelled = SubprocessExitCodeNormalizationPolicy.Evaluate("build", 1, "linux", cancelled: true);
        var timeout = SubprocessExitCodeNormalizationPolicy.Evaluate("build", 1, "macos", timedOut: true);

        Assert.Equal("success", success.Classification);
        Assert.Equal("windows", success.Platform);
        Assert.False(success.Retryable);
        Assert.Equal("cancelled", cancelled.Classification);
        Assert.Equal("unix", cancelled.Platform);
        Assert.False(cancelled.Retryable);
        Assert.Equal("timeout", timeout.Classification);
        Assert.True(timeout.Retryable);
    }

    [Fact]
    public void Evaluate_ClassifiesBuiltInCustomRetryableAndPermanentFailures()
    {
        var unix = SubprocessExitCodeNormalizationPolicy.Evaluate("test", 75, "unix");
        var custom = SubprocessExitCodeNormalizationPolicy.Evaluate("test", 7, "windows", retryableExitCodes: new[] { 7 });
        var permanent = SubprocessExitCodeNormalizationPolicy.Evaluate("test", 2, "unix");
        Assert.True(unix.Retryable);
        Assert.True(custom.Retryable);
        Assert.Equal("retryable-failure", custom.Classification);
        Assert.False(permanent.Retryable);
        Assert.Equal("permanent-failure", permanent.Classification);
        Assert.Equal(64, permanent.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_RejectsInvalidIdentityPlatformAndOutOfRangeCode()
    {
        Assert.Throws<ArgumentException>(() => SubprocessExitCodeNormalizationPolicy.Evaluate("bad process!", 0, "windows"));
        Assert.Throws<ArgumentException>(() => SubprocessExitCodeNormalizationPolicy.Evaluate("build", 0, "dos"));
        Assert.Throws<ArgumentOutOfRangeException>(() => SubprocessExitCodeNormalizationPolicy.Evaluate("build", 70_000, "windows"));
    }
}

public sealed class LogChronologyIntegrityPolicyTests
{
    [Fact]
    public void Evaluate_NormalizesDetectsBackwardJumpsAndComputesHealth()
    {
        var start = new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.FromHours(3));
        var decision = LogChronologyIntegrityPolicy.Evaluate(new[]
        {
            new LogChronologyEvent("event-a", start, 0),
            new LogChronologyEvent("event-b", start.AddSeconds(-10), 1),
            new LogChronologyEvent("event-c", start.AddHours(-2), 2)
        }, TimeSpan.FromSeconds(5));

        Assert.Equal(2, decision.OutOfOrderCount);
        Assert.Equal(1, decision.ImpossibleBackwardJumpCount);
        Assert.Equal(50, decision.HealthScore);
        Assert.Equal("event-c", decision.CanonicalEvents[0].Identity);
        Assert.All(decision.CanonicalEvents, item => Assert.Equal(TimeSpan.Zero, item.Timestamp.Offset));
        Assert.Equal("log-chronology-invalid", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_PreservesDeterministicEqualTimeOrderAndClampsJitter()
    {
        var now = DateTimeOffset.UtcNow;
        var decision = LogChronologyIntegrityPolicy.Evaluate(new[]
        {
            new LogChronologyEvent("b", now, 1),
            new LogChronologyEvent("a", now, 0)
        }, TimeSpan.FromDays(1));
        Assert.Equal(LogChronologyIntegrityPolicy.MaxJitter, decision.ToleratedJitter);
        Assert.Equal(new[] { "a", "b" }, decision.CanonicalEvents.Select(item => item.Identity).ToArray());
        Assert.Equal(100, decision.HealthScore);
        Assert.Equal("log-chronology-valid", decision.ReasonCode);
    }

    [Fact]
    public void Evaluate_RejectsDuplicatesAndNegativeSequence()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(() => LogChronologyIntegrityPolicy.Evaluate(new[]
        {
            new LogChronologyEvent("event", now, 0), new LogChronologyEvent("EVENT", now, 1)
        }, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => LogChronologyIntegrityPolicy.Evaluate(new[] { new LogChronologyEvent("event", now, -1) }, TimeSpan.Zero));
    }
}

public sealed class BuildOutputManifestVerificationPolicyTests
{
    private static readonly string HashA = new('a', 64);
    private static readonly string HashB = new('b', 64);

    [Fact]
    public void Evaluate_DetectsMissingHashAndSizeMismatchesDeterministically()
    {
        var decision = BuildOutputManifestVerificationPolicy.Evaluate(new[]
        {
            new BuildOutputExpectation("apk", "bin\\app.apk", HashA.ToUpperInvariant(), 10),
            new BuildOutputExpectation("symbols", "bin/symbols.zip", HashA, 20)
        }, new[]
        {
            new ObservedBuildOutput("bin/app.apk", HashB, 11)
        });

        Assert.False(decision.Valid);
        Assert.Equal(0, decision.VerifiedCount);
        Assert.Equal(new[] { "apk:hash-mismatch", "apk:size-mismatch", "symbols:missing" }, decision.Findings.Select(f => $"{f.OutputIdentity}:{f.FindingType}").ToArray());
        Assert.Equal("build-output-manifest-invalid", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_VerifiesMatchingManifest()
    {
        var decision = BuildOutputManifestVerificationPolicy.Evaluate(new[] { new BuildOutputExpectation("apk", "bin/app.apk", HashA, 10) }, new[] { new ObservedBuildOutput("bin\\app.apk", HashA, 10) });
        Assert.True(decision.Valid);
        Assert.Equal(1, decision.VerifiedCount);
        Assert.Empty(decision.Findings);
        Assert.Equal("build-output-manifest-valid", decision.ReasonCode);
    }

    [Fact]
    public void Evaluate_RejectsUnsafePathInvalidHashNegativeSizeAndDuplicateExpectedPath()
    {
        Assert.Throws<ArgumentException>(() => BuildOutputManifestVerificationPolicy.Evaluate(new[] { new BuildOutputExpectation("apk", "../app.apk", HashA, 1) }, Array.Empty<ObservedBuildOutput>()));
        Assert.Throws<ArgumentException>(() => BuildOutputManifestVerificationPolicy.Evaluate(new[] { new BuildOutputExpectation("apk", "app.apk", "bad", 1) }, Array.Empty<ObservedBuildOutput>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildOutputManifestVerificationPolicy.Evaluate(new[] { new BuildOutputExpectation("apk", "app.apk", HashA, -1) }, Array.Empty<ObservedBuildOutput>()));
        Assert.Throws<ArgumentException>(() => BuildOutputManifestVerificationPolicy.Evaluate(new[]
        {
            new BuildOutputExpectation("a", "app.apk", HashA, 1), new BuildOutputExpectation("b", "app.apk", HashA, 1)
        }, Array.Empty<ObservedBuildOutput>()));
    }
}

public sealed class DiagnosticCorrelationWindowPolicyTests
{
    [Fact]
    public void Evaluate_NormalizesGroupsByWindowAndPreservesHighestSeverity()
    {
        var now = new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.FromHours(3));
        var decision = DiagnosticCorrelationWindowPolicy.Evaluate(new[]
        {
            new CorrelationDiagnostic("D2", "BUILD", now.AddSeconds(5), 80),
            new CorrelationDiagnostic("d1", "build", now, 10),
            new CorrelationDiagnostic("d3", "build", now.AddSeconds(60), 120)
        }, TimeSpan.FromSeconds(10));

        Assert.Equal(2, decision.Groups.Count);
        Assert.Equal(new[] { "d1", "d2" }, decision.Groups[0].DiagnosticIds);
        Assert.Equal(80, decision.Groups[0].HighestSeverity);
        Assert.Equal(100, decision.Groups[1].HighestSeverity);
        Assert.Equal("diagnostic-correlation-grouped", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_ClampsWindowAndBoundsGroupSize()
    {
        var now = DateTimeOffset.UtcNow;
        var decision = DiagnosticCorrelationWindowPolicy.Evaluate(new[]
        {
            new CorrelationDiagnostic("a", "key", now, 1), new CorrelationDiagnostic("b", "key", now.AddMilliseconds(1), 2)
        }, TimeSpan.Zero, 1);
        Assert.Equal(DiagnosticCorrelationWindowPolicy.MinWindow, decision.CorrelationWindow);
        Assert.Equal(2, decision.Groups.Count);
        Assert.All(decision.Groups, group => Assert.Single(group.DiagnosticIds));
    }

    [Fact]
    public void Evaluate_RejectsDuplicateDiagnosticIdentityAndSupportsEmptyInput()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(() => DiagnosticCorrelationWindowPolicy.Evaluate(new[]
        {
            new CorrelationDiagnostic("d", "key", now, 1), new CorrelationDiagnostic("D", "key", now, 2)
        }, TimeSpan.FromSeconds(5)));
        var empty = DiagnosticCorrelationWindowPolicy.Evaluate(Array.Empty<CorrelationDiagnostic>(), TimeSpan.FromSeconds(5));
        Assert.Empty(empty.Groups);
        Assert.Equal("diagnostic-correlation-empty", empty.ReasonCode);
    }
}

public sealed class ResourcePressureAdmissionControlPolicyTests
{
    [Fact]
    public void Evaluate_ClampsMetricsComputesAggregateAndDefersHighPressureWork()
    {
        var decision = ResourcePressureAdmissionControlPolicy.Evaluate(new[]
        {
            new WorkloadResourcePressure("LOW", 10, 20, 30, 5),
            new WorkloadResourcePressure("high", 200, 50, 40, 200)
        }, 80);

        Assert.Equal(1, decision.AdmittedCount);
        Assert.Equal(1, decision.DeferredCount);
        Assert.Equal("low", decision.Workloads[0].Identity);
        Assert.True(decision.Workloads[0].Admitted);
        Assert.Equal(100d, decision.Workloads[1].AggregatePressure);
        Assert.Equal(100, decision.Workloads[1].Priority);
        Assert.False(decision.Workloads[1].Admitted);
        Assert.Equal("resource-admission-partial", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_ClampsThresholdAndReturnsClearOrDeferredReasons()
    {
        var clear = ResourcePressureAdmissionControlPolicy.Evaluate(new[] { new WorkloadResourcePressure("work", 0, 0, 0, 0) }, 0);
        var deferred = ResourcePressureAdmissionControlPolicy.Evaluate(new[] { new WorkloadResourcePressure("work", 100, 100, 100, 0) }, 100);
        Assert.Equal(1d, clear.AdmissionThreshold);
        Assert.Equal("resource-admission-clear", clear.ReasonCode);
        Assert.Equal("resource-admission-deferred", deferred.ReasonCode);
    }

    [Fact]
    public void Evaluate_RejectsNegativeMetricsAndDuplicateWorkloadIdentity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ResourcePressureAdmissionControlPolicy.Evaluate(new[] { new WorkloadResourcePressure("work", -1, 0, 0, 0) }));
        Assert.Throws<ArgumentException>(() => ResourcePressureAdmissionControlPolicy.Evaluate(new[]
        {
            new WorkloadResourcePressure("work", 1, 1, 1, 0), new WorkloadResourcePressure("WORK", 2, 2, 2, 0)
        }));
    }
}

public sealed class ReleaseHandoffIntegrityPolicyTests
{
    private static readonly string Hash = new('c', 64);

    [Fact]
    public void Evaluate_NormalizesTimestampSurfacesFailedMandatoryEvidenceAndComputesScore()
    {
        var timestamp = new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.FromHours(3));
        var decision = ReleaseHandoffIntegrityPolicy.Evaluate("REL-1", "QA", Hash.ToUpperInvariant(), timestamp, new[]
        {
            new ReleaseHandoffEvidence("build", true, true),
            new ReleaseHandoffEvidence("tests", false, true)
        }, new[] { "tests", "build" });

        Assert.Equal("rel-1", decision.ReleaseIdentity);
        Assert.Equal("qa", decision.Stage);
        Assert.Equal(TimeSpan.Zero, decision.HandoffTimestampUtc.Offset);
        Assert.Empty(decision.MissingCategories);
        Assert.Equal(new[] { "tests" }, decision.FailedMandatoryCategories);
        Assert.Equal(50, decision.CompletenessScore);
        Assert.False(decision.Complete);
        Assert.Equal("release-handoff-incomplete", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_ReportsMissingEvidenceAndCompletesWhenAllRequiredPass()
    {
        var now = DateTimeOffset.UtcNow;
        var missing = ReleaseHandoffIntegrityPolicy.Evaluate("rel", "staging", Hash, now, new[] { new ReleaseHandoffEvidence("build", true, true) }, new[] { "build", "tests" });
        var complete = ReleaseHandoffIntegrityPolicy.Evaluate("rel", "production", Hash, now, new[]
        {
            new ReleaseHandoffEvidence("build", true, true), new ReleaseHandoffEvidence("tests", true, true)
        }, new[] { "build", "tests" });
        Assert.Equal(new[] { "tests" }, missing.MissingCategories);
        Assert.False(missing.Complete);
        Assert.Equal(100, complete.CompletenessScore);
        Assert.True(complete.Complete);
        Assert.Equal("release-handoff-complete", complete.ReasonCode);
    }

    [Fact]
    public void Evaluate_RejectsInvalidStageFingerprintAndDuplicateEvidence()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(() => ReleaseHandoffIntegrityPolicy.Evaluate("rel", "invalid", Hash, now, Array.Empty<ReleaseHandoffEvidence>(), Array.Empty<string>()));
        Assert.Throws<ArgumentException>(() => ReleaseHandoffIntegrityPolicy.Evaluate("rel", "qa", "bad", now, Array.Empty<ReleaseHandoffEvidence>(), Array.Empty<string>()));
        Assert.Throws<ArgumentException>(() => ReleaseHandoffIntegrityPolicy.Evaluate("rel", "qa", Hash, now, new[]
        {
            new ReleaseHandoffEvidence("tests", true, true), new ReleaseHandoffEvidence("TESTS", true, true)
        }, new[] { "tests" }));
    }
}
