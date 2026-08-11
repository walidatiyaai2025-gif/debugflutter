using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record BuildProvenanceRequest(
    string CommitSha,
    string Branch,
    bool IsDirty,
    string ToolchainFingerprint,
    string Target,
    string Mode,
    DateTimeOffset BuiltAt);

public sealed record BuildProvenanceDecision(
    string CommitSha,
    string Branch,
    bool IsDirty,
    string ToolchainFingerprint,
    string Target,
    string Mode,
    DateTimeOffset BuiltAtUtc,
    string CanonicalPayload,
    string ReasonCode,
    string Fingerprint);

public static partial class BuildProvenancePolicy
{
    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitPattern();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex FingerprintPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9._/-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex BranchPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex TargetPattern();

    public static BuildProvenanceDecision Evaluate(BuildProvenanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var commit = NormalizeCommitSha(request.CommitSha);
        var branch = NormalizeBranch(request.Branch);
        var toolchain = NormalizeToolchainFingerprint(request.ToolchainFingerprint);
        var target = NormalizeTarget(request.Target);
        var mode = NormalizeMode(request.Mode);
        var timestamp = request.BuiltAt.ToUniversalTime();

        var payload = string.Join('|', commit, branch, request.IsDirty ? "dirty" : "clean", toolchain, target, mode, timestamp.ToString("O"));
        return new BuildProvenanceDecision(
            commit,
            branch,
            request.IsDirty,
            toolchain,
            target,
            mode,
            timestamp,
            payload,
            "build-provenance-valid",
            Hash(payload));
    }

    public static string NormalizeCommitSha(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (!CommitPattern().IsMatch(normalized))
        {
            throw new ArgumentException("Repository commit SHA must be 40 hexadecimal characters.", nameof(value));
        }
        return normalized;
    }

    public static string NormalizeToolchainFingerprint(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (!FingerprintPattern().IsMatch(normalized))
        {
            throw new ArgumentException("Toolchain fingerprint must be SHA-256.", nameof(value));
        }
        return normalized;
    }

    private static string NormalizeBranch(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().Replace('\\', '/').ToLowerInvariant();
        if (!BranchPattern().IsMatch(normalized) || normalized.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Branch identity is invalid.", nameof(value));
        }
        return normalized;
    }

    private static string NormalizeTarget(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (!TargetPattern().IsMatch(normalized))
        {
            throw new ArgumentException("Build target is invalid.", nameof(value));
        }
        return normalized;
    }

    private static string NormalizeMode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized is not ("debug" or "profile" or "release"))
        {
            throw new ArgumentException("Build mode must be debug, profile, or release.", nameof(value));
        }
        return normalized;
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
