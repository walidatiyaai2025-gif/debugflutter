using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Integrity;

public sealed record ChecksumVerificationRequest(
    bool Exists,
    string ExpectedSha256,
    string ActualSha256,
    long ActualBytes,
    long? ExpectedBytes,
    DateTimeOffset VerifiedAt);

public sealed record ChecksumVerificationDecision(
    bool Verified,
    string ExpectedSha256,
    string ActualSha256,
    long ActualBytes,
    long? ExpectedBytes,
    DateTimeOffset VerifiedAtUtc,
    string ReasonCode,
    string Fingerprint);

public static partial class ChecksumVerificationPolicy
{
    public static ChecksumVerificationDecision Evaluate(ChecksumVerificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var expected = NormalizeSha256(request.ExpectedSha256);
        var actual = NormalizeSha256(request.ActualSha256);
        if (request.ActualBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Artifact size must be positive.");
        }
        if (request.ExpectedBytes is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Expected size must be positive when supplied.");
        }

        var exists = request.Exists;
        var hashMatches = string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
        var sizeMatches = request.ExpectedBytes is null || request.ExpectedBytes.Value == request.ActualBytes;
        var verified = exists && hashMatches && sizeMatches;
        var reason = !exists ? "artifact-missing"
            : !hashMatches ? "checksum-mismatch"
            : !sizeMatches ? "size-mismatch"
            : "verified";
        var verifiedAtUtc = request.VerifiedAt.ToUniversalTime();
        var canonical = string.Join('|', exists, expected, actual, request.ActualBytes, request.ExpectedBytes?.ToString() ?? string.Empty, verifiedAtUtc.ToString("O"), reason);

        return new ChecksumVerificationDecision(verified, expected, actual, request.ActualBytes, request.ExpectedBytes, verifiedAtUtc, reason, Hash(canonical));
    }

    public static string NormalizeSha256(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (!Sha256Regex().IsMatch(normalized))
        {
            throw new ArgumentException("SHA-256 checksum must contain exactly 64 hexadecimal characters.", nameof(value));
        }
        return normalized;
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [GeneratedRegex("^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();
}
