using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Releases;

public enum ReleaseChannel
{
    Development = 0,
    Internal = 1,
    Beta = 2,
    Production = 3
}

public sealed record ReleasePromotionRequest(
    ReleaseChannel SourceChannel,
    ReleaseChannel TargetChannel,
    bool ArtifactVerified,
    bool QualityGatesPassed,
    string ArtifactFingerprint,
    string ExpectedArtifactFingerprint,
    string BuildMode,
    DateTimeOffset PromotedAt);

public sealed record ReleasePromotionDecision(
    bool Allowed,
    ReleaseChannel SourceChannel,
    ReleaseChannel TargetChannel,
    DateTimeOffset PromotedAtUtc,
    string BuildMode,
    string ReasonCode,
    string Fingerprint);

public static partial class ReleasePromotionPolicy
{
    public static ReleasePromotionDecision Evaluate(ReleasePromotionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateChannel(request.SourceChannel, nameof(request.SourceChannel));
        ValidateChannel(request.TargetChannel, nameof(request.TargetChannel));
        var artifactFingerprint = NormalizeFingerprint(request.ArtifactFingerprint);
        var expectedFingerprint = NormalizeFingerprint(request.ExpectedArtifactFingerprint);
        var buildMode = NormalizeBuildMode(request.BuildMode);
        var promotedAtUtc = request.PromotedAt.ToUniversalTime();

        var forward = request.TargetChannel > request.SourceChannel;
        var fingerprintsMatch = string.Equals(artifactFingerprint, expectedFingerprint, StringComparison.OrdinalIgnoreCase);
        var productionModeOk = request.TargetChannel != ReleaseChannel.Production || buildMode == "release";
        var allowed = forward && request.ArtifactVerified && request.QualityGatesPassed && fingerprintsMatch && productionModeOk;

        var reason = !forward ? "backward-or-same-promotion-denied"
            : !request.ArtifactVerified ? "artifact-not-verified"
            : !request.QualityGatesPassed ? "quality-gates-failed"
            : !fingerprintsMatch ? "artifact-fingerprint-mismatch"
            : !productionModeOk ? "production-requires-release-mode"
            : "promotion-approved";

        var canonical = string.Join('|', request.SourceChannel, request.TargetChannel, request.ArtifactVerified, request.QualityGatesPassed,
            artifactFingerprint, expectedFingerprint, buildMode, promotedAtUtc.ToString("O"), reason);
        return new ReleasePromotionDecision(allowed, request.SourceChannel, request.TargetChannel, promotedAtUtc, buildMode, reason, Hash(canonical));
    }

    public static void ValidateChannel(ReleaseChannel channel, string parameterName)
    {
        if (!Enum.IsDefined(channel)) throw new ArgumentOutOfRangeException(parameterName, channel, "Release channel is invalid.");
    }

    public static string NormalizeFingerprint(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (!FingerprintRegex().IsMatch(normalized)) throw new ArgumentException("Artifact fingerprint must be a 64-character hexadecimal value.", nameof(value));
        return normalized;
    }

    public static string NormalizeBuildMode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized is not ("debug" or "profile" or "release")) throw new ArgumentException("Build mode is invalid.", nameof(value));
        return normalized;
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [GeneratedRegex("^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex FingerprintRegex();
}
