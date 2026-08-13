using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

internal static class B1450PolicyHelpers
{
    private static readonly Regex IdentityPattern = new("^[a-z0-9][a-z0-9._-]{0,127}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Sha256Pattern = new("^[a-f0-9]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SemVerPattern = new("^(0|[1-9]\\d*)\\.(0|[1-9]\\d*)\\.(0|[1-9]\\d*)(?:-[0-9A-Za-z.-]+)?(?:\\+[0-9A-Za-z.-]+)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string NormalizeIdentity(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Identity is required.", paramName);
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (!IdentityPattern.IsMatch(normalized))
        {
            throw new ArgumentException($"Unsafe identity '{value}'.", paramName);
        }

        return normalized;
    }

    public static string NormalizeHash(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("SHA-256 hash is required.", paramName);
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (!Sha256Pattern.IsMatch(normalized))
        {
            throw new ArgumentException("Expected a 64-character hexadecimal SHA-256 hash.", paramName);
        }

        return normalized;
    }

    public static string NormalizeSemVer(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Semantic version is required.", paramName);
        }

        var normalized = value.Trim();
        if (!SemVerPattern.IsMatch(normalized))
        {
            throw new ArgumentException($"Invalid semantic version '{value}'.", paramName);
        }

        return normalized;
    }

    public static string NormalizeRelativePath(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Relative path is required.", paramName);
        }

        var normalized = value.Trim().Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal) || Regex.IsMatch(normalized, "^[A-Za-z]:"))
        {
            throw new ArgumentException("Rooted paths are not allowed.", paramName);
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException("Path traversal and empty paths are not allowed.", paramName);
        }

        return string.Join('/', segments);
    }

    public static DateTimeOffset Utc(DateTimeOffset value) => value.ToUniversalTime();

    public static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed record ClockSkewSample(string Identity, DateTimeOffset ObservedAt, DateTimeOffset LocalTime, DateTimeOffset ReferenceTime, TimeSpan Uncertainty);
public sealed record ClockSkewSampleState(string Identity, DateTimeOffset ObservedAtUtc, TimeSpan EffectiveSkew, string Confidence);
public sealed record ClockSkewConfidenceDecision(IReadOnlyList<ClockSkewSampleState> Samples, TimeSpan AllowableSkew, TimeSpan WorstEffectiveSkew, string Confidence, bool ResynchronizationRequired, string ReasonCode, string Fingerprint);

public static class ClockSkewConfidencePolicy
{
    public static readonly TimeSpan MinAllowableSkew = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaxAllowableSkew = TimeSpan.FromMinutes(10);

    public static ClockSkewConfidenceDecision Evaluate(IEnumerable<ClockSkewSample> samples, TimeSpan allowableSkew)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var allowed = allowableSkew < MinAllowableSkew ? MinAllowableSkew : allowableSkew > MaxAllowableSkew ? MaxAllowableSkew : allowableSkew;
        var normalized = samples.Select(sample =>
        {
            ArgumentNullException.ThrowIfNull(sample);
            if (sample.Uncertainty < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(samples), "Clock uncertainty cannot be negative.");
            }

            var identity = B1450PolicyHelpers.NormalizeIdentity(sample.Identity, nameof(sample.Identity));
            var observed = B1450PolicyHelpers.Utc(sample.ObservedAt);
            var local = B1450PolicyHelpers.Utc(sample.LocalTime);
            var reference = B1450PolicyHelpers.Utc(sample.ReferenceTime);
            var rawTicks = Math.Abs((local - reference).Ticks);
            var effectiveTicks = Math.Max(0L, rawTicks - sample.Uncertainty.Ticks);
            var effective = TimeSpan.FromTicks(effectiveTicks);
            var confidence = effective <= allowed ? "trusted" : effective <= TimeSpan.FromTicks(allowed.Ticks * 2) ? "warn" : "untrusted";
            return new ClockSkewSampleState(identity, observed, effective, confidence);
        }).OrderBy(sample => sample.ObservedAtUtc).ThenBy(sample => sample.Identity, StringComparer.Ordinal).ToArray();

        if (normalized.Length == 0)
        {
            throw new ArgumentException("At least one clock sample is required.", nameof(samples));
        }
        if (normalized.GroupBy(sample => sample.Identity, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Duplicate clock-sample identities are not allowed.", nameof(samples));
        }

        var worst = normalized.Max(sample => sample.EffectiveSkew);
        var confidence = normalized.Any(sample => sample.Confidence == "untrusted") ? "untrusted" : normalized.Any(sample => sample.Confidence == "warn") ? "warn" : "trusted";
        var resync = confidence != "trusted";
        var reason = $"clock-confidence-{confidence}";
        var payload = $"{allowed.Ticks}|{worst.Ticks}|{confidence}|{string.Join(";", normalized.Select(sample => $"{sample.Identity}:{sample.ObservedAtUtc:O}:{sample.EffectiveSkew.Ticks}:{sample.Confidence}"))}";
        return new ClockSkewConfidenceDecision(normalized, allowed, worst, confidence, resync, reason, B1450PolicyHelpers.Hash(payload));
    }
}

public sealed record DnsResolutionSafetyDecision(string EndpointIdentity, string Hostname, IReadOnlyList<string> ResolvedAddresses, bool ResolutionDrifted, string PreferredAddressClass, string ReasonCode, string Fingerprint);

public static class DnsResolutionSafetyPolicy
{
    public static DnsResolutionSafetyDecision Evaluate(string endpointIdentity, string hostname, IEnumerable<string> resolvedAddresses, IEnumerable<string>? previousAddresses = null)
    {
        ArgumentNullException.ThrowIfNull(resolvedAddresses);
        var endpoint = B1450PolicyHelpers.NormalizeIdentity(endpointIdentity, nameof(endpointIdentity));
        var host = (hostname ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();
        if (host.Length == 0 || Uri.CheckHostName(host) != UriHostNameType.Dns)
        {
            throw new ArgumentException($"Invalid DNS hostname '{hostname}'.", nameof(hostname));
        }

        static IPAddress Parse(string value)
        {
            if (!IPAddress.TryParse((value ?? string.Empty).Trim(), out var address))
            {
                throw new ArgumentException($"Invalid resolved IP address '{value}'.", nameof(resolvedAddresses));
            }
            return address;
        }

        var current = resolvedAddresses.Select(Parse).Distinct().OrderByDescending(IsPrivate).ThenBy(address => address.ToString(), StringComparer.Ordinal).ToArray();
        if (current.Length == 0)
        {
            throw new ArgumentException("At least one resolved address is required.", nameof(resolvedAddresses));
        }

        var previous = previousAddresses?.Select(Parse).Distinct().OrderBy(address => address.ToString(), StringComparer.Ordinal).ToArray() ?? Array.Empty<IPAddress>();
        var currentSet = current.Select(address => address.ToString()).ToHashSet(StringComparer.Ordinal);
        var previousSet = previous.Select(address => address.ToString()).ToHashSet(StringComparer.Ordinal);
        var drifted = previous.Length > 0 && !currentSet.SetEquals(previousSet);
        var preferredClass = IsPrivate(current[0]) ? "private" : "public";
        var reason = drifted ? "dns-resolution-drifted" : "dns-resolution-stable";
        var addresses = current.Select(address => address.ToString()).ToArray();
        var payload = $"{endpoint}|{host}|{preferredClass}|{drifted}|{string.Join(',', addresses)}";
        return new DnsResolutionSafetyDecision(endpoint, host, addresses, drifted, preferredClass, reason, B1450PolicyHelpers.Hash(payload));
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) || (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 169 && bytes[1] == 254);
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return address.IsIPv6LinkLocal || (bytes[0] & 0xFE) == 0xFC;
        }
        return false;
    }
}

public sealed record PackageVulnerabilityAdvisory(string AdvisoryIdentity, double Severity, bool FixedVersionAvailable);
public sealed record PackageVulnerabilityEvidenceDecision(string PackageIdentity, string PackageVersion, IReadOnlyList<PackageVulnerabilityAdvisory> Advisories, double HighestSeverity, bool Blocked, string ReasonCode, string Fingerprint);

public static class PackageVulnerabilityEvidencePolicy
{
    public static PackageVulnerabilityEvidenceDecision Evaluate(string packageIdentity, string packageVersion, IEnumerable<PackageVulnerabilityAdvisory> advisories, double criticalThreshold = 9.0)
    {
        ArgumentNullException.ThrowIfNull(advisories);
        var package = B1450PolicyHelpers.NormalizeIdentity(packageIdentity, nameof(packageIdentity));
        var version = B1450PolicyHelpers.NormalizeSemVer(packageVersion, nameof(packageVersion));
        var threshold = Math.Clamp(criticalThreshold, 0d, 10d);
        var normalized = advisories.Select(advisory =>
        {
            ArgumentNullException.ThrowIfNull(advisory);
            if (advisory.Severity < 0d || double.IsNaN(advisory.Severity) || double.IsInfinity(advisory.Severity))
            {
                throw new ArgumentOutOfRangeException(nameof(advisories), "Vulnerability severity must be finite and non-negative.");
            }
            return advisory with
            {
                AdvisoryIdentity = B1450PolicyHelpers.NormalizeIdentity(advisory.AdvisoryIdentity, nameof(advisory.AdvisoryIdentity)),
                Severity = Math.Clamp(advisory.Severity, 0d, 10d)
            };
        }).GroupBy(advisory => advisory.AdvisoryIdentity, StringComparer.Ordinal)
          .Select(group => group.OrderByDescending(item => item.Severity).ThenByDescending(item => item.FixedVersionAvailable).First())
          .OrderByDescending(advisory => advisory.Severity)
          .ThenBy(advisory => advisory.AdvisoryIdentity, StringComparer.Ordinal)
          .ToArray();

        var highest = normalized.Length == 0 ? 0d : normalized.Max(advisory => advisory.Severity);
        var blocked = highest >= threshold && normalized.Length > 0;
        var reason = blocked ? "package-vulnerability-blocked" : normalized.Length == 0 ? "package-vulnerability-clear" : "package-vulnerability-observed";
        var payload = $"{package}|{version}|{threshold:F2}|{highest:F2}|{blocked}|{string.Join(';', normalized.Select(item => $"{item.AdvisoryIdentity}:{item.Severity:F2}:{item.FixedVersionAvailable}"))}";
        return new PackageVulnerabilityEvidenceDecision(package, version, normalized, highest, blocked, reason, B1450PolicyHelpers.Hash(payload));
    }
}

public sealed record FilesystemPortabilityFinding(string Path, string FindingType);
public sealed record FilesystemCasePortabilityDecision(IReadOnlyList<string> CanonicalPaths, IReadOnlyList<FilesystemPortabilityFinding> Findings, bool Portable, string ReasonCode, string Fingerprint);

public static class FilesystemCasePortabilityPolicy
{
    private static readonly Regex ReservedNamePattern = new("^(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\\.|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static FilesystemCasePortabilityDecision Evaluate(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var normalized = paths.Select(path => B1450PolicyHelpers.NormalizeRelativePath(path, nameof(paths))).ToArray();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("At least one path is required.", nameof(paths));
        }

        var findings = new List<FilesystemPortabilityFinding>();
        foreach (var group in normalized.GroupBy(path => path.ToLowerInvariant(), StringComparer.Ordinal).Where(group => group.Select(item => item).Distinct(StringComparer.Ordinal).Count() > 1))
        {
            foreach (var path in group.OrderBy(path => path, StringComparer.Ordinal))
            {
                findings.Add(new FilesystemPortabilityFinding(path, "case-collision"));
            }
        }

        foreach (var path in normalized)
        {
            if (path.Split('/').Any(segment => ReservedNamePattern.IsMatch(segment)))
            {
                findings.Add(new FilesystemPortabilityFinding(path, "reserved-name"));
            }
        }

        var canonical = normalized.Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var orderedFindings = findings.Distinct().OrderBy(finding => finding.Path, StringComparer.Ordinal).ThenBy(finding => finding.FindingType, StringComparer.Ordinal).ToArray();
        var portable = orderedFindings.Length == 0;
        var reason = portable ? "filesystem-portable" : "filesystem-portability-conflict";
        var payload = $"{string.Join(';', canonical)}|{string.Join(';', orderedFindings.Select(f => $"{f.Path}:{f.FindingType}"))}";
        return new FilesystemCasePortabilityDecision(canonical, orderedFindings, portable, reason, B1450PolicyHelpers.Hash(payload));
    }
}

public sealed record SubprocessExitNormalizationDecision(string ProcessIdentity, string Platform, int RawExitCode, string Classification, bool Retryable, string ReasonCode, string Fingerprint);

public static class SubprocessExitCodeNormalizationPolicy
{
    public static SubprocessExitNormalizationDecision Evaluate(string processIdentity, int rawExitCode, string platform, bool cancelled = false, bool timedOut = false, IEnumerable<int>? retryableExitCodes = null)
    {
        var process = B1450PolicyHelpers.NormalizeIdentity(processIdentity, nameof(processIdentity));
        if (rawExitCode < -65535 || rawExitCode > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(rawExitCode), "Exit code is outside the supported normalized range.");
        }

        var normalizedPlatform = (platform ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedPlatform is not ("windows" or "unix" or "linux" or "macos"))
        {
            throw new ArgumentException($"Unsupported process platform '{platform}'.", nameof(platform));
        }
        if (normalizedPlatform is "linux" or "macos") normalizedPlatform = "unix";

        var retryableCodes = (retryableExitCodes ?? Array.Empty<int>()).ToHashSet();
        string classification;
        bool retryable;
        if (timedOut)
        {
            classification = "timeout";
            retryable = true;
        }
        else if (cancelled)
        {
            classification = "cancelled";
            retryable = false;
        }
        else if (rawExitCode == 0)
        {
            classification = "success";
            retryable = false;
        }
        else if (retryableCodes.Contains(rawExitCode) || (normalizedPlatform == "unix" && rawExitCode == 75) || (normalizedPlatform == "windows" && rawExitCode == 1460))
        {
            classification = "retryable-failure";
            retryable = true;
        }
        else
        {
            classification = "permanent-failure";
            retryable = false;
        }

        var reason = $"process-exit-{classification}";
        var payload = $"{process}|{normalizedPlatform}|{rawExitCode}|{classification}|{retryable}";
        return new SubprocessExitNormalizationDecision(process, normalizedPlatform, rawExitCode, classification, retryable, reason, B1450PolicyHelpers.Hash(payload));
    }
}

public sealed record LogChronologyEvent(string Identity, DateTimeOffset Timestamp, int Sequence);
public sealed record LogChronologyDecision(IReadOnlyList<LogChronologyEvent> CanonicalEvents, TimeSpan ToleratedJitter, int OutOfOrderCount, int ImpossibleBackwardJumpCount, int HealthScore, string ReasonCode, string Fingerprint);

public static class LogChronologyIntegrityPolicy
{
    public static readonly TimeSpan MaxJitter = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ImpossibleBackwardJump = TimeSpan.FromHours(1);

    public static LogChronologyDecision Evaluate(IEnumerable<LogChronologyEvent> events, TimeSpan toleratedJitter)
    {
        ArgumentNullException.ThrowIfNull(events);
        var jitter = toleratedJitter < TimeSpan.Zero ? TimeSpan.Zero : toleratedJitter > MaxJitter ? MaxJitter : toleratedJitter;
        var normalized = events.Select(item =>
        {
            ArgumentNullException.ThrowIfNull(item);
            if (item.Sequence < 0) throw new ArgumentOutOfRangeException(nameof(events), "Log sequence cannot be negative.");
            return item with { Identity = B1450PolicyHelpers.NormalizeIdentity(item.Identity, nameof(item.Identity)), Timestamp = B1450PolicyHelpers.Utc(item.Timestamp) };
        }).ToArray();
        if (normalized.Length == 0) throw new ArgumentException("At least one log event is required.", nameof(events));
        if (normalized.GroupBy(item => item.Identity, StringComparer.Ordinal).Any(group => group.Count() > 1)) throw new ArgumentException("Duplicate log-event identities are not allowed.", nameof(events));

        var sourceOrder = normalized.OrderBy(item => item.Sequence).ThenBy(item => item.Identity, StringComparer.Ordinal).ToArray();
        var outOfOrder = 0;
        var impossible = 0;
        for (var i = 1; i < sourceOrder.Length; i++)
        {
            var backward = sourceOrder[i - 1].Timestamp - sourceOrder[i].Timestamp;
            if (backward > jitter) outOfOrder++;
            if (backward > ImpossibleBackwardJump) impossible++;
        }

        var canonical = normalized.OrderBy(item => item.Timestamp).ThenBy(item => item.Sequence).ThenBy(item => item.Identity, StringComparer.Ordinal).ToArray();
        var health = Math.Clamp(100 - (outOfOrder * 10) - (impossible * 30), 0, 100);
        var reason = impossible > 0 ? "log-chronology-invalid" : outOfOrder > 0 ? "log-chronology-degraded" : "log-chronology-valid";
        var payload = $"{jitter.Ticks}|{outOfOrder}|{impossible}|{health}|{string.Join(';', canonical.Select(item => $"{item.Identity}:{item.Timestamp:O}:{item.Sequence}"))}";
        return new LogChronologyDecision(canonical, jitter, outOfOrder, impossible, health, reason, B1450PolicyHelpers.Hash(payload));
    }
}

public sealed record BuildOutputExpectation(string Identity, string RelativePath, string ExpectedSha256, long ExpectedSizeBytes);
public sealed record ObservedBuildOutput(string RelativePath, string Sha256, long SizeBytes);
public sealed record BuildOutputManifestFinding(string OutputIdentity, string FindingType);
public sealed record BuildOutputManifestDecision(IReadOnlyList<BuildOutputManifestFinding> Findings, int VerifiedCount, bool Valid, string ReasonCode, string Fingerprint);

public static class BuildOutputManifestVerificationPolicy
{
    public static BuildOutputManifestDecision Evaluate(IEnumerable<BuildOutputExpectation> expectedOutputs, IEnumerable<ObservedBuildOutput> observedOutputs)
    {
        ArgumentNullException.ThrowIfNull(expectedOutputs);
        ArgumentNullException.ThrowIfNull(observedOutputs);
        var expected = expectedOutputs.Select(item =>
        {
            ArgumentNullException.ThrowIfNull(item);
            if (item.ExpectedSizeBytes < 0) throw new ArgumentOutOfRangeException(nameof(expectedOutputs), "Expected output size cannot be negative.");
            return item with
            {
                Identity = B1450PolicyHelpers.NormalizeIdentity(item.Identity, nameof(item.Identity)),
                RelativePath = B1450PolicyHelpers.NormalizeRelativePath(item.RelativePath, nameof(item.RelativePath)),
                ExpectedSha256 = B1450PolicyHelpers.NormalizeHash(item.ExpectedSha256, nameof(item.ExpectedSha256))
            };
        }).ToArray();
        if (expected.GroupBy(item => item.Identity, StringComparer.Ordinal).Any(group => group.Count() > 1) || expected.GroupBy(item => item.RelativePath, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new ArgumentException("Duplicate expected output identities or paths are not allowed.", nameof(expectedOutputs));

        var observed = observedOutputs.Select(item =>
        {
            ArgumentNullException.ThrowIfNull(item);
            if (item.SizeBytes < 0) throw new ArgumentOutOfRangeException(nameof(observedOutputs), "Observed output size cannot be negative.");
            return item with
            {
                RelativePath = B1450PolicyHelpers.NormalizeRelativePath(item.RelativePath, nameof(item.RelativePath)),
                Sha256 = B1450PolicyHelpers.NormalizeHash(item.Sha256, nameof(item.Sha256))
            };
        }).ToArray();
        if (observed.GroupBy(item => item.RelativePath, StringComparer.Ordinal).Any(group => group.Count() > 1)) throw new ArgumentException("Duplicate observed output paths are not allowed.", nameof(observedOutputs));

        var observedMap = observed.ToDictionary(item => item.RelativePath, StringComparer.Ordinal);
        var findings = new List<BuildOutputManifestFinding>();
        var verified = 0;
        foreach (var item in expected.OrderBy(item => item.Identity, StringComparer.Ordinal))
        {
            if (!observedMap.TryGetValue(item.RelativePath, out var actual))
            {
                findings.Add(new BuildOutputManifestFinding(item.Identity, "missing"));
                continue;
            }
            var mismatch = false;
            if (!string.Equals(item.ExpectedSha256, actual.Sha256, StringComparison.Ordinal))
            {
                findings.Add(new BuildOutputManifestFinding(item.Identity, "hash-mismatch"));
                mismatch = true;
            }
            if (item.ExpectedSizeBytes != actual.SizeBytes)
            {
                findings.Add(new BuildOutputManifestFinding(item.Identity, "size-mismatch"));
                mismatch = true;
            }
            if (!mismatch) verified++;
        }
        var ordered = findings.OrderBy(finding => finding.OutputIdentity, StringComparer.Ordinal).ThenBy(finding => finding.FindingType, StringComparer.Ordinal).ToArray();
        var valid = ordered.Length == 0;
        var reason = valid ? "build-output-manifest-valid" : "build-output-manifest-invalid";
        var payload = $"{verified}|{valid}|{string.Join(';', ordered.Select(f => $"{f.OutputIdentity}:{f.FindingType}"))}";
        return new BuildOutputManifestDecision(ordered, verified, valid, reason, B1450PolicyHelpers.Hash(payload));
    }
}

public sealed record CorrelationDiagnostic(string Identity, string CorrelationKey, DateTimeOffset Timestamp, int Severity);
public sealed record DiagnosticCorrelationGroup(string CorrelationKey, int GroupIndex, IReadOnlyList<string> DiagnosticIds, int HighestSeverity);
public sealed record DiagnosticCorrelationDecision(IReadOnlyList<DiagnosticCorrelationGroup> Groups, TimeSpan CorrelationWindow, int MaxGroupSize, string ReasonCode, string Fingerprint);

public static class DiagnosticCorrelationWindowPolicy
{
    public static readonly TimeSpan MinWindow = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaxWindow = TimeSpan.FromMinutes(30);

    public static DiagnosticCorrelationDecision Evaluate(IEnumerable<CorrelationDiagnostic> diagnostics, TimeSpan correlationWindow, int maxGroupSize = 64)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        var window = correlationWindow < MinWindow ? MinWindow : correlationWindow > MaxWindow ? MaxWindow : correlationWindow;
        var groupLimit = Math.Clamp(maxGroupSize, 1, 256);
        var normalized = diagnostics.Select(item =>
        {
            ArgumentNullException.ThrowIfNull(item);
            return item with
            {
                Identity = B1450PolicyHelpers.NormalizeIdentity(item.Identity, nameof(item.Identity)),
                CorrelationKey = B1450PolicyHelpers.NormalizeIdentity(item.CorrelationKey, nameof(item.CorrelationKey)),
                Timestamp = B1450PolicyHelpers.Utc(item.Timestamp),
                Severity = Math.Clamp(item.Severity, 0, 100)
            };
        }).ToArray();
        if (normalized.GroupBy(item => item.Identity, StringComparer.Ordinal).Any(group => group.Count() > 1)) throw new ArgumentException("Duplicate diagnostic identities are not allowed.", nameof(diagnostics));

        var groups = new List<DiagnosticCorrelationGroup>();
        foreach (var keyGroup in normalized.GroupBy(item => item.CorrelationKey, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var ordered = keyGroup.OrderBy(item => item.Timestamp).ThenBy(item => item.Identity, StringComparer.Ordinal).ToArray();
            var current = new List<CorrelationDiagnostic>();
            var index = 0;
            DateTimeOffset? previous = null;
            foreach (var item in ordered)
            {
                if (current.Count > 0 && (item.Timestamp - previous!.Value > window || current.Count >= groupLimit))
                {
                    groups.Add(CreateGroup(keyGroup.Key, index++, current));
                    current.Clear();
                }
                current.Add(item);
                previous = item.Timestamp;
            }
            if (current.Count > 0) groups.Add(CreateGroup(keyGroup.Key, index, current));
        }

        var reason = groups.Count == 0 ? "diagnostic-correlation-empty" : "diagnostic-correlation-grouped";
        var payload = $"{window.Ticks}|{groupLimit}|{string.Join(';', groups.Select(group => $"{group.CorrelationKey}:{group.GroupIndex}:{group.HighestSeverity}:{string.Join(',', group.DiagnosticIds)}"))}";
        return new DiagnosticCorrelationDecision(groups, window, groupLimit, reason, B1450PolicyHelpers.Hash(payload));
    }

    private static DiagnosticCorrelationGroup CreateGroup(string key, int index, IReadOnlyCollection<CorrelationDiagnostic> items)
        => new(key, index, items.Select(item => item.Identity).OrderBy(id => id, StringComparer.Ordinal).ToArray(), items.Max(item => item.Severity));
}

public sealed record WorkloadResourcePressure(string Identity, double CpuPercent, double MemoryPercent, double DiskPercent, int Priority);
public sealed record WorkloadAdmissionState(string Identity, double AggregatePressure, int Priority, bool Admitted);
public sealed record ResourcePressureAdmissionDecision(IReadOnlyList<WorkloadAdmissionState> Workloads, double AdmissionThreshold, int AdmittedCount, int DeferredCount, string ReasonCode, string Fingerprint);

public static class ResourcePressureAdmissionControlPolicy
{
    public static ResourcePressureAdmissionDecision Evaluate(IEnumerable<WorkloadResourcePressure> workloads, double admissionThreshold = 80d)
    {
        ArgumentNullException.ThrowIfNull(workloads);
        if (double.IsNaN(admissionThreshold) || double.IsInfinity(admissionThreshold)) throw new ArgumentOutOfRangeException(nameof(admissionThreshold));
        var threshold = Math.Clamp(admissionThreshold, 1d, 100d);
        var normalized = workloads.Select(item =>
        {
            ArgumentNullException.ThrowIfNull(item);
            if (item.CpuPercent < 0d || item.MemoryPercent < 0d || item.DiskPercent < 0d || double.IsNaN(item.CpuPercent) || double.IsNaN(item.MemoryPercent) || double.IsNaN(item.DiskPercent) || double.IsInfinity(item.CpuPercent) || double.IsInfinity(item.MemoryPercent) || double.IsInfinity(item.DiskPercent))
                throw new ArgumentOutOfRangeException(nameof(workloads), "Resource pressure metrics must be finite and non-negative.");
            var identity = B1450PolicyHelpers.NormalizeIdentity(item.Identity, nameof(item.Identity));
            var cpu = Math.Clamp(item.CpuPercent, 0d, 100d);
            var memory = Math.Clamp(item.MemoryPercent, 0d, 100d);
            var disk = Math.Clamp(item.DiskPercent, 0d, 100d);
            var aggregate = Math.Max(cpu, Math.Max(memory, disk));
            return new WorkloadAdmissionState(identity, aggregate, Math.Clamp(item.Priority, 0, 100), aggregate < threshold);
        }).ToArray();
        if (normalized.GroupBy(item => item.Identity, StringComparer.Ordinal).Any(group => group.Count() > 1)) throw new ArgumentException("Duplicate workload identities are not allowed.", nameof(workloads));

        var ordered = normalized.OrderBy(item => item.Admitted ? 0 : 1)
            .ThenByDescending(item => item.Admitted ? item.Priority : item.AggregatePressure)
            .ThenByDescending(item => item.Priority)
            .ThenBy(item => item.Identity, StringComparer.Ordinal)
            .ToArray();
        var admitted = ordered.Count(item => item.Admitted);
        var deferred = ordered.Length - admitted;
        var reason = deferred == 0 ? "resource-admission-clear" : admitted == 0 ? "resource-admission-deferred" : "resource-admission-partial";
        var payload = $"{threshold:F2}|{admitted}|{deferred}|{string.Join(';', ordered.Select(item => $"{item.Identity}:{item.AggregatePressure:F2}:{item.Priority}:{item.Admitted}"))}";
        return new ResourcePressureAdmissionDecision(ordered, threshold, admitted, deferred, reason, B1450PolicyHelpers.Hash(payload));
    }
}

public sealed record ReleaseHandoffEvidence(string Category, bool Passed, bool Mandatory);
public sealed record ReleaseHandoffIntegrityDecision(string ReleaseIdentity, string Stage, string ArtifactFingerprint, DateTimeOffset HandoffTimestampUtc, IReadOnlyList<string> MissingCategories, IReadOnlyList<string> FailedMandatoryCategories, int CompletenessScore, bool Complete, string ReasonCode, string Fingerprint);

public static class ReleaseHandoffIntegrityPolicy
{
    private static readonly HashSet<string> AllowedStages = new(StringComparer.Ordinal) { "build", "qa", "staging", "production" };

    public static ReleaseHandoffIntegrityDecision Evaluate(string releaseIdentity, string stage, string artifactFingerprint, DateTimeOffset handoffTimestamp, IEnumerable<ReleaseHandoffEvidence> evidence, IEnumerable<string> requiredCategories)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(requiredCategories);
        var release = B1450PolicyHelpers.NormalizeIdentity(releaseIdentity, nameof(releaseIdentity));
        var normalizedStage = (stage ?? string.Empty).Trim().ToLowerInvariant();
        if (!AllowedStages.Contains(normalizedStage)) throw new ArgumentException($"Unsupported handoff stage '{stage}'.", nameof(stage));
        var artifact = B1450PolicyHelpers.NormalizeHash(artifactFingerprint, nameof(artifactFingerprint));
        var timestamp = B1450PolicyHelpers.Utc(handoffTimestamp);
        var normalizedEvidence = evidence.Select(item =>
        {
            ArgumentNullException.ThrowIfNull(item);
            return item with { Category = B1450PolicyHelpers.NormalizeIdentity(item.Category, nameof(item.Category)) };
        }).ToArray();
        if (normalizedEvidence.GroupBy(item => item.Category, StringComparer.Ordinal).Any(group => group.Count() > 1)) throw new ArgumentException("Duplicate handoff evidence categories are not allowed.", nameof(evidence));

        var required = requiredCategories.Select(category => B1450PolicyHelpers.NormalizeIdentity(category, nameof(requiredCategories))).Distinct(StringComparer.Ordinal).OrderBy(category => category, StringComparer.Ordinal).ToArray();
        var evidenceMap = normalizedEvidence.ToDictionary(item => item.Category, StringComparer.Ordinal);
        var missing = required.Where(category => !evidenceMap.ContainsKey(category)).ToArray();
        var failedMandatory = normalizedEvidence.Where(item => item.Mandatory && !item.Passed).Select(item => item.Category).OrderBy(category => category, StringComparer.Ordinal).ToArray();
        var passedRequired = required.Count(category => evidenceMap.TryGetValue(category, out var item) && item.Passed);
        var score = required.Length == 0 ? 100 : (int)Math.Round((double)passedRequired * 100d / required.Length, MidpointRounding.AwayFromZero);
        score = Math.Clamp(score, 0, 100);
        var complete = missing.Length == 0 && failedMandatory.Length == 0 && passedRequired == required.Length;
        var reason = complete ? "release-handoff-complete" : "release-handoff-incomplete";
        var payload = $"{release}|{normalizedStage}|{artifact}|{timestamp:O}|{score}|{complete}|{string.Join(',', missing)}|{string.Join(',', failedMandatory)}";
        return new ReleaseHandoffIntegrityDecision(release, normalizedStage, artifact, timestamp, missing, failedMandatory, score, complete, reason, B1450PolicyHelpers.Hash(payload));
    }
}
