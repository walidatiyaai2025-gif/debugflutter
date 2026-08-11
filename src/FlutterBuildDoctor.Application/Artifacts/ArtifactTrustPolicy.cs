using System.Security.Cryptography;
using System.Text;

namespace FlutterBuildDoctor.Application.Artifacts;

public enum TrustedArtifactKind
{
    Apk = 0,
    Aab = 1
}

public enum TrustedBuildMode
{
    Debug = 0,
    Profile = 1,
    Release = 2
}

public sealed record ArtifactTrustEvidence(
    string Path,
    bool Exists,
    long? SizeBytes,
    string? Sha256,
    TrustedArtifactKind Kind,
    TrustedBuildMode Mode,
    string? BuildId = null,
    DateTimeOffset? CreatedAt = null);

public sealed record ArtifactTrustResult(
    bool Exists,
    bool SizeValid,
    bool Sha256Valid,
    bool ModeValid,
    int TrustScore,
    bool Verified,
    string ProvenanceFingerprint,
    IReadOnlyList<string> EvidenceLines);

public static class ArtifactTrustPolicy
{
    public static ArtifactTrustResult Evaluate(ArtifactTrustEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var path = ValidatePath(evidence.Path);
        if (!Enum.IsDefined(evidence.Kind)) throw new ArgumentOutOfRangeException(nameof(evidence.Kind));
        if (!Enum.IsDefined(evidence.Mode)) throw new ArgumentOutOfRangeException(nameof(evidence.Mode));

        var sizeValid = evidence.Exists && evidence.SizeBytes is > 0;
        var shaValid = IsValidSha256(evidence.Sha256);
        var modeValid = evidence.Kind == TrustedArtifactKind.Apk || evidence.Mode == TrustedBuildMode.Release;
        var buildIdValid = !string.IsNullOrWhiteSpace(evidence.BuildId) &&
                           evidence.BuildId!.Length <= 128 &&
                           !evidence.BuildId.Any(char.IsControl);
        var createdValid = evidence.CreatedAt is not null;

        var score = 0;
        if (evidence.Exists) score += 25;
        if (sizeValid) score += 20;
        if (shaValid) score += 35;
        if (buildIdValid) score += 10;
        if (createdValid) score += 10;
        if (!modeValid) score = Math.Min(score, 40);
        score = Math.Clamp(score, 0, 100);

        var verified = evidence.Exists && sizeValid && shaValid && modeValid;
        var createdUtc = evidence.CreatedAt?.ToUniversalTime();
        var fingerprint = Fingerprint(path, evidence, sizeValid, shaValid, modeValid, createdUtc);
        var lines = new[]
        {
            $"path={path}",
            $"kind={evidence.Kind.ToString().ToLowerInvariant()}",
            $"mode={evidence.Mode.ToString().ToLowerInvariant()}",
            $"exists={evidence.Exists.ToString().ToLowerInvariant()}",
            $"size={evidence.SizeBytes?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}",
            $"sha256={(shaValid ? evidence.Sha256!.ToLowerInvariant() : "invalid")}",
            $"buildId={(buildIdValid ? evidence.BuildId!.Trim() : "unknown")}",
            $"createdAt={(createdUtc?.ToString("O", System.Globalization.CultureInfo.InvariantCulture) ?? "unknown")}",
            $"score={score}",
            $"verified={verified.ToString().ToLowerInvariant()}"
        };

        return new ArtifactTrustResult(
            evidence.Exists,
            sizeValid,
            shaValid,
            modeValid,
            score,
            verified,
            fingerprint,
            lines);
    }

    public static bool IsValidSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 64) return false;
        return value.All(character =>
            character is >= '0' and <= '9' or
            >= 'a' and <= 'f' or
            >= 'A' and <= 'F');
    }

    private static string ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1024 || path.Any(char.IsControl))
        {
            throw new ArgumentException("Artifact path is invalid.", nameof(path));
        }

        return path.Trim();
    }

    private static string Fingerprint(
        string path,
        ArtifactTrustEvidence evidence,
        bool sizeValid,
        bool shaValid,
        bool modeValid,
        DateTimeOffset? createdUtc)
    {
        var canonical = string.Join('|', new[]
        {
            path,
            ((int)evidence.Kind).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((int)evidence.Mode).ToString(System.Globalization.CultureInfo.InvariantCulture),
            evidence.Exists ? "1" : "0",
            evidence.SizeBytes?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            shaValid ? evidence.Sha256!.ToLowerInvariant() : string.Empty,
            evidence.BuildId?.Trim() ?? string.Empty,
            createdUtc?.ToString("O", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            sizeValid ? "1" : "0",
            modeValid ? "1" : "0"
        });

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
