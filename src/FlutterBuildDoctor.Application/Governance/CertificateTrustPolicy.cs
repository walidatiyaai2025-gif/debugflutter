using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record CertificateTrustEvidence(
    string Thumbprint,
    string Subject,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter,
    bool ChainTrusted,
    string ExpectedHost,
    IReadOnlyCollection<string> PresentedHosts);

public sealed record CertificateTrustDecision(
    bool Trusted,
    string Thumbprint,
    string Subject,
    DateTimeOffset NotBeforeUtc,
    DateTimeOffset NotAfterUtc,
    string ReasonCode,
    string Fingerprint);

public static partial class CertificateTrustPolicy
{
    public static CertificateTrustDecision Evaluate(CertificateTrustEvidence evidence, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var thumbprint = NormalizeThumbprint(evidence.Thumbprint);
        var subject = NormalizeSubject(evidence.Subject);
        var notBefore = evidence.NotBefore.ToUniversalTime();
        var notAfter = evidence.NotAfter.ToUniversalTime();
        if (notAfter <= notBefore) throw new ArgumentException("Certificate validity window is invalid.", nameof(evidence));

        var now = observedAt.ToUniversalTime();
        var expectedHost = NormalizeHost(evidence.ExpectedHost);
        var presentedHosts = evidence.PresentedHosts?.Select(NormalizeHost)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray() ?? Array.Empty<string>();

        var hostMatch = presentedHosts.Any(host => HostMatches(expectedHost, host));
        var reason = now < notBefore ? "certificate-not-yet-valid"
            : now > notAfter ? "certificate-expired"
            : !evidence.ChainTrusted ? "certificate-chain-untrusted"
            : !hostMatch ? "certificate-host-mismatch"
            : "certificate-trusted";
        var trusted = reason == "certificate-trusted";
        var canonical = string.Join('|', thumbprint, subject, notBefore.ToString("O"), notAfter.ToString("O"), evidence.ChainTrusted, expectedHost, string.Join(',', presentedHosts), reason);
        return new CertificateTrustDecision(trusted, thumbprint, subject, notBefore, notAfter, reason, Hash(canonical));
    }

    public static string NormalizeThumbprint(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        if (!ThumbprintRegex().IsMatch(normalized)) throw new ArgumentException("Certificate thumbprint is malformed.", nameof(value));
        return normalized;
    }

    public static string NormalizeSubject(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Any(char.IsControl)) throw new ArgumentException("Certificate subject contains control characters.", nameof(value));
        return WhitespaceRegex().Replace(value.Trim(), " ");
    }

    public static string NormalizeHost(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Any(char.IsControl)) throw new ArgumentException("Certificate host evidence contains control characters.", nameof(value));
        var normalized = value.Trim().TrimEnd('.').ToLowerInvariant();
        if (!HostRegex().IsMatch(normalized)) throw new ArgumentException("Certificate host evidence is invalid.", nameof(value));
        return normalized;
    }

    private static bool HostMatches(string expected, string presented)
    {
        if (string.Equals(expected, presented, StringComparison.OrdinalIgnoreCase)) return true;
        if (!presented.StartsWith("*.", StringComparison.Ordinal)) return false;
        var suffix = presented[1..];
        if (!expected.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return false;
        var prefix = expected[..^suffix.Length];
        return prefix.Length > 0 && !prefix.Contains('.');
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [GeneratedRegex("^(?:[0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.CultureInvariant)]
    private static partial Regex ThumbprintRegex();

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("^(?:\\*\\.)?[a-z0-9](?:[a-z0-9.-]{0,251}[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex HostRegex();
}
