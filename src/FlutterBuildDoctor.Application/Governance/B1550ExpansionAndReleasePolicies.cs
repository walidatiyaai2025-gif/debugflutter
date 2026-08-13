using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record EnvironmentExpansionVariable(string Name, string Value, bool Secret);
public sealed record ExpandedEnvironmentVariable(string Name, string RedactedValue, bool Secret);
public sealed record EnvironmentExpansionSafetyDecision(IReadOnlyList<ExpandedEnvironmentVariable> Variables, int MaximumDepth, bool SecretsRedacted, string ReasonCode, string Fingerprint);

public static class EnvironmentExpansionSafetyPolicy
{
    private static readonly Regex VariablePattern = new("^[A-Z_][A-Z0-9_]{0,127}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ReferencePattern = new("\\$\\{([A-Za-z_][A-Za-z0-9_]*)\\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    public const int MaximumSupportedDepth = 16;

    public static EnvironmentExpansionSafetyDecision Evaluate(IEnumerable<EnvironmentExpansionVariable> variables, int maximumDepth = 8)
    {
        ArgumentNullException.ThrowIfNull(variables);
        var depthLimit = Math.Clamp(maximumDepth, 1, MaximumSupportedDepth);
        var normalized = variables.Select(variable =>
        {
            ArgumentNullException.ThrowIfNull(variable);
            var name = (variable.Name ?? string.Empty).Trim().ToUpperInvariant();
            if (!VariablePattern.IsMatch(name)) throw new ArgumentException($"Unsafe variable name '{variable.Name}'.", nameof(variables));
            return new EnvironmentExpansionVariable(name, variable.Value ?? string.Empty, variable.Secret);
        }).ToArray();
        if (normalized.GroupBy(variable => variable.Name, StringComparer.Ordinal).Any(group => group.Count() > 1)) throw new ArgumentException("Duplicate environment variables are not allowed.", nameof(variables));
        var map = normalized.ToDictionary(variable => variable.Name, StringComparer.Ordinal);
        IEnumerable<string> References(string value) => ReferencePattern.Matches(value).Cast<Match>().Select(match => match.Groups[1].Value.ToUpperInvariant());
        foreach (var variable in normalized)
            foreach (var reference in References(variable.Value))
                if (!map.ContainsKey(reference)) throw new ArgumentException($"Unknown environment reference '{reference}'.", nameof(variables));

        var memo = new Dictionary<string, string>(StringComparer.Ordinal);
        string Expand(string name, int depth, HashSet<string> stack)
        {
            if (memo.TryGetValue(name, out var cached)) return cached;
            if (depth > depthLimit) throw new InvalidOperationException("Environment expansion depth exceeded.");
            if (!stack.Add(name)) throw new InvalidOperationException("Recursive environment expansion detected.");
            var expanded = ReferencePattern.Replace(map[name].Value, match => Expand(match.Groups[1].Value.ToUpperInvariant(), depth + 1, stack));
            stack.Remove(name);
            memo[name] = expanded;
            return expanded;
        }
        foreach (var name in map.Keys.OrderBy(name => name, StringComparer.Ordinal)) _ = Expand(name, 1, new HashSet<string>(StringComparer.Ordinal));
        var secretValues = normalized.Where(variable => variable.Secret).Select(variable => memo[variable.Name]).Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).OrderByDescending(value => value.Length).ToArray();
        string Redact(string value)
        {
            var redacted = value;
            foreach (var secret in secretValues) redacted = redacted.Replace(secret, "[redacted]", StringComparison.Ordinal);
            return redacted;
        }
        var expandedVariables = normalized.OrderBy(variable => variable.Name, StringComparer.Ordinal).Select(variable => new ExpandedEnvironmentVariable(variable.Name, Redact(memo[variable.Name]), variable.Secret)).ToArray();
        var payload = $"{depthLimit}|{string.Join(';', expandedVariables.Select(v => $"{v.Name}:{v.RedactedValue}:{v.Secret}"))}";
        return new EnvironmentExpansionSafetyDecision(expandedVariables, depthLimit, secretValues.Length > 0, "environment-expansion-safe", B1550PolicyHelpers.Fingerprint(payload));
    }
}

public sealed record CacheKeyDescriptor(string Namespace, string Platform, string ToolchainVersion, string DependencyFingerprint, string Salt);
public sealed record CacheKeyCompatibilityDecision(CacheKeyDescriptor Requested, string CanonicalKey, bool CompatibleWithExisting, bool CrossPlatformMismatch, string ReasonCode, string Fingerprint);

public static class CacheKeyCompatibilityPolicy
{
    private static readonly HashSet<string> AllowedPlatforms = new(StringComparer.Ordinal) { "windows", "linux", "macos", "android" };

    public static CacheKeyCompatibilityDecision Evaluate(string cacheNamespace, string platform, string toolchainVersion, string dependencyFingerprint, string? salt, CacheKeyDescriptor? existing = null)
    {
        var requested = Normalize(cacheNamespace, platform, toolchainVersion, dependencyFingerprint, salt);
        var normalizedExisting = existing is null ? null : Normalize(existing.Namespace, existing.Platform, existing.ToolchainVersion, existing.DependencyFingerprint, existing.Salt);
        var compatible = normalizedExisting is null || requested == normalizedExisting;
        var crossPlatform = normalizedExisting is not null && requested.Platform != normalizedExisting.Platform;
        var canonical = $"{requested.Namespace}:{requested.Platform}:{requested.ToolchainVersion}:{requested.DependencyFingerprint[..16]}:{requested.Salt}";
        var reason = compatible ? "cache-key-compatible" : crossPlatform ? "cache-key-platform-mismatch" : "cache-key-incompatible";
        var payload = $"{canonical}|{normalizedExisting}|{compatible}|{crossPlatform}";
        return new CacheKeyCompatibilityDecision(requested, canonical, compatible, crossPlatform, reason, B1550PolicyHelpers.Fingerprint(payload));
    }

    private static CacheKeyDescriptor Normalize(string cacheNamespace, string platform, string toolchainVersion, string dependencyFingerprint, string? salt)
    {
        var ns = B1550PolicyHelpers.Identity(cacheNamespace, nameof(cacheNamespace));
        var normalizedPlatform = (platform ?? string.Empty).Trim().ToLowerInvariant();
        if (!AllowedPlatforms.Contains(normalizedPlatform)) throw new ArgumentException($"Unsupported cache platform '{platform}'.", nameof(platform));
        if (!Version.TryParse((toolchainVersion ?? string.Empty).Trim(), out var version)) throw new ArgumentException("Invalid toolchain version.", nameof(toolchainVersion));
        var minor = version.Minor < 0 ? 0 : version.Minor;
        var build = version.Build < 0 ? 0 : version.Build;
        var normalizedVersion = $"{version.Major}.{minor}.{build}";
        var hash = B1550PolicyHelpers.HashValue(dependencyFingerprint, nameof(dependencyFingerprint));
        var normalizedSalt = string.IsNullOrWhiteSpace(salt) ? "default" : B1550PolicyHelpers.Identity(salt, nameof(salt));
        return new CacheKeyDescriptor(ns, normalizedPlatform, normalizedVersion, hash, normalizedSalt);
    }
}

public sealed record DependencyLicenseEvidence(string DependencyIdentity, string LicenseIdentifier);
public sealed record DependencyLicenseFinding(string DependencyIdentity, string LicenseIdentifier, string Classification);
public sealed record DependencyLicenseEvidenceDecision(IReadOnlyList<DependencyLicenseFinding> Findings, bool Blocked, int UnknownCount, string ReasonCode, string Fingerprint);

public static class DependencyLicenseEvidencePolicy
{
    public static DependencyLicenseEvidenceDecision Evaluate(IEnumerable<DependencyLicenseEvidence> evidence, IEnumerable<string> approvedLicenses, IEnumerable<string> deniedLicenses)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(approvedLicenses);
        ArgumentNullException.ThrowIfNull(deniedLicenses);
        static string NormalizeLicense(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("License identifier is required.", nameof(value));
            return value.Trim().ToUpperInvariant();
        }
        var approved = approvedLicenses.Select(NormalizeLicense).ToHashSet(StringComparer.Ordinal);
        var denied = deniedLicenses.Select(NormalizeLicense).ToHashSet(StringComparer.Ordinal);
        if (approved.Overlaps(denied)) throw new ArgumentException("A license cannot be both approved and denied.");
        var normalized = evidence.Select(item =>
        {
            ArgumentNullException.ThrowIfNull(item);
            return new DependencyLicenseEvidence(B1550PolicyHelpers.Identity(item.DependencyIdentity, nameof(item.DependencyIdentity)), NormalizeLicense(item.LicenseIdentifier));
        }).Distinct().OrderBy(item => item.DependencyIdentity, StringComparer.Ordinal).ThenBy(item => item.LicenseIdentifier, StringComparer.Ordinal).ToArray();
        var findings = normalized.Select(item => new DependencyLicenseFinding(item.DependencyIdentity, item.LicenseIdentifier, denied.Contains(item.LicenseIdentifier) ? "denied" : approved.Contains(item.LicenseIdentifier) ? "approved" : "unknown")).ToArray();
        var blocked = findings.Any(finding => finding.Classification == "denied");
        var unknown = findings.Count(finding => finding.Classification == "unknown");
        var reason = blocked ? "dependency-license-denied" : unknown > 0 ? "dependency-license-unknown" : "dependency-license-compliant";
        var payload = $"{blocked}|{unknown}|{string.Join(';', findings.Select(f => $"{f.DependencyIdentity}:{f.LicenseIdentifier}:{f.Classification}"))}";
        return new DependencyLicenseEvidenceDecision(findings, blocked, unknown, reason, B1550PolicyHelpers.Fingerprint(payload));
    }
}

public sealed record DiagnosticAttachment(string Identity, string AttachmentClass, long SizeBytes, bool Mandatory, int Priority);
public sealed record DiagnosticAttachmentQuotaDecision(IReadOnlyList<DiagnosticAttachment> Retained, IReadOnlyList<string> DroppedAttachmentIds, long RetainedBytes, long TotalQuotaBytes, long PerAttachmentQuotaBytes, string ReasonCode, string Fingerprint);

public static class DiagnosticAttachmentQuotaPolicy
{
    public const long MinTotalQuota = 1L * 1024 * 1024;
    public const long MaxTotalQuota = 1L * 1024 * 1024 * 1024;
    public const long MinPerAttachmentQuota = 64L * 1024;
    public const long MaxPerAttachmentQuota = 256L * 1024 * 1024;

    public static DiagnosticAttachmentQuotaDecision Evaluate(IEnumerable<DiagnosticAttachment> attachments, long totalQuotaBytes, long perAttachmentQuotaBytes)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        var totalQuota = Math.Clamp(totalQuotaBytes, MinTotalQuota, MaxTotalQuota);
        var perQuota = Math.Min(totalQuota, Math.Clamp(perAttachmentQuotaBytes, MinPerAttachmentQuota, MaxPerAttachmentQuota));
        var normalized = attachments.Select(attachment =>
        {
            ArgumentNullException.ThrowIfNull(attachment);
            if (attachment.SizeBytes < 0 || attachment.Priority < 0) throw new ArgumentOutOfRangeException(nameof(attachments));
            return new DiagnosticAttachment(B1550PolicyHelpers.Identity(attachment.Identity, nameof(attachment.Identity)), B1550PolicyHelpers.Identity(attachment.AttachmentClass, nameof(attachment.AttachmentClass)), attachment.SizeBytes, attachment.Mandatory, attachment.Priority);
        }).ToArray();
        if (normalized.GroupBy(attachment => attachment.Identity, StringComparer.Ordinal).Any(group => group.Count() > 1)) throw new ArgumentException("Duplicate attachment identities are not allowed.", nameof(attachments));
        var ordered = normalized.OrderByDescending(attachment => attachment.Mandatory).ThenByDescending(attachment => attachment.Priority).ThenBy(attachment => attachment.Identity, StringComparer.Ordinal).ToArray();
        var retained = new List<DiagnosticAttachment>();
        var dropped = new List<string>();
        long used = 0;
        foreach (var attachment in ordered)
        {
            var fits = attachment.SizeBytes <= perQuota && attachment.SizeBytes <= totalQuota - used;
            if (fits) { retained.Add(attachment); used += attachment.SizeBytes; }
            else dropped.Add(attachment.Identity);
        }
        dropped.Sort(StringComparer.Ordinal);
        var reason = dropped.Count == 0 ? "diagnostic-attachment-quota-complete" : "diagnostic-attachment-quota-trimmed";
        var payload = $"{totalQuota}|{perQuota}|{used}|{string.Join(',', retained.Select(a => a.Identity))}|{string.Join(',', dropped)}";
        return new DiagnosticAttachmentQuotaDecision(retained, dropped, used, totalQuota, perQuota, reason, B1550PolicyHelpers.Fingerprint(payload));
    }
}

public sealed record ReleaseSigningStep(string StepIdentity, int Sequence, string SignerIdentity, string? ParentStepIdentity, string ArtifactFingerprint, bool TrustedRootSigner);
public sealed record ReleaseSigningChainDecision(string ReleaseIdentity, IReadOnlyList<ReleaseSigningStep> OrderedSteps, bool CompleteChain, string RootSignerIdentity, string ReasonCode, string Fingerprint);

public static class ReleaseSigningChainIntegrityPolicy
{
    public static ReleaseSigningChainDecision Evaluate(string releaseIdentity, IEnumerable<ReleaseSigningStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var release = B1550PolicyHelpers.Identity(releaseIdentity, nameof(releaseIdentity));
        var normalized = steps.Select(step =>
        {
            ArgumentNullException.ThrowIfNull(step);
            if (step.Sequence < 0) throw new ArgumentOutOfRangeException(nameof(steps));
            var identity = B1550PolicyHelpers.Identity(step.StepIdentity, nameof(step.StepIdentity));
            var signer = B1550PolicyHelpers.Identity(step.SignerIdentity, nameof(step.SignerIdentity));
            var parent = step.ParentStepIdentity is null ? null : B1550PolicyHelpers.Identity(step.ParentStepIdentity, nameof(step.ParentStepIdentity));
            if (identity == parent) throw new ArgumentException("Signing step cannot parent itself.", nameof(steps));
            return new ReleaseSigningStep(identity, step.Sequence, signer, parent, B1550PolicyHelpers.HashValue(step.ArtifactFingerprint, nameof(step.ArtifactFingerprint)), step.TrustedRootSigner);
        }).OrderBy(step => step.Sequence).ThenBy(step => step.StepIdentity, StringComparer.Ordinal).ToArray();
        if (normalized.Length == 0) throw new ArgumentException("At least one signing step is required.", nameof(steps));
        if (normalized.GroupBy(step => step.StepIdentity, StringComparer.Ordinal).Any(group => group.Count() > 1)) throw new ArgumentException("Duplicate signing step identities are not allowed.", nameof(steps));
        if (normalized.GroupBy(step => step.Sequence).Any(group => group.Count() > 1)) throw new ArgumentException("Duplicate signing sequence numbers are not allowed.", nameof(steps));
        var map = normalized.ToDictionary(step => step.StepIdentity, StringComparer.Ordinal);
        foreach (var step in normalized.Where(step => step.ParentStepIdentity is not null))
            if (!map.ContainsKey(step.ParentStepIdentity!)) throw new ArgumentException($"Broken parent signature reference '{step.ParentStepIdentity}'.", nameof(steps));
        if (normalized.Select(step => step.ArtifactFingerprint).Distinct(StringComparer.Ordinal).Count() != 1) throw new ArgumentException("All signing steps must bind the same artifact fingerprint.", nameof(steps));
        var root = normalized[0];
        if (root.ParentStepIdentity is not null || !root.TrustedRootSigner) throw new InvalidOperationException("Signing chain must begin with a trusted root signer.");
        for (var index = 1; index < normalized.Length; index++)
            if (normalized[index].ParentStepIdentity != normalized[index - 1].StepIdentity) throw new InvalidOperationException("Signing chain order is broken.");
        var payload = $"{release}|{root.SignerIdentity}|{string.Join(';', normalized.Select(s => $"{s.Sequence}:{s.StepIdentity}:{s.SignerIdentity}:{s.ParentStepIdentity}:{s.ArtifactFingerprint}:{s.TrustedRootSigner}"))}";
        return new ReleaseSigningChainDecision(release, normalized, true, root.SignerIdentity, "release-signing-chain-complete", B1550PolicyHelpers.Fingerprint(payload));
    }
}
