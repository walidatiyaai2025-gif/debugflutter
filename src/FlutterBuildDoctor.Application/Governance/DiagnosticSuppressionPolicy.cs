using System.Security.Cryptography;
using System.Text;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record DiagnosticSuppressionRule(
    string Signature,
    string Scope,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    bool Permanent);

public sealed record SuppressionDecision(
    string Signature,
    string Scope,
    DateTimeOffset? EffectiveExpiryUtc,
    bool Suppressed,
    bool Expired,
    string ReasonCode,
    string Fingerprint);

public static class DiagnosticSuppressionPolicy
{
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(30);

    public static SuppressionDecision Evaluate(
        DiagnosticSuppressionRule rule,
        string evidenceSignature,
        string evidenceScope,
        string severity,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var signature = NormalizeToken(rule.Signature, nameof(rule.Signature));
        var scope = NormalizeToken(rule.Scope, nameof(rule.Scope));
        var evidenceSig = NormalizeToken(evidenceSignature, nameof(evidenceSignature));
        var evidenceScopeNormalized = NormalizeToken(evidenceScope, nameof(evidenceScope));
        var severityNormalized = NormalizeToken(severity, nameof(severity));
        var created = rule.CreatedAtUtc.ToUniversalTime();
        var nowUtc = now.ToUniversalTime();

        DateTimeOffset? expiry = rule.Permanent
            ? null
            : (rule.ExpiresAtUtc ?? created + MaxLifetime).ToUniversalTime();
        if (expiry.HasValue && expiry.Value - created > MaxLifetime)
            expiry = created + MaxLifetime;

        var expired = expiry.HasValue && nowUtc >= expiry.Value;
        var exactMatch = signature == evidenceSig && scope == evidenceScopeNormalized;
        var blockerPermanentDenied = rule.Permanent && severityNormalized == "blocker";
        var suppressed = exactMatch && !expired && !blockerPermanentDenied;
        var reason = blockerPermanentDenied
            ? "blocker-suppression-denied"
            : expired
                ? "suppression-expired"
                : suppressed
                    ? "diagnostic-suppressed"
                    : "diagnostic-not-suppressed";

        var payload = $"{signature}|{scope}|{created:O}|{expiry:O}|{rule.Permanent}|{evidenceSig}|{evidenceScopeNormalized}|{severityNormalized}|{suppressed}|{reason}";
        return new SuppressionDecision(signature, scope, expiry, suppressed, expired, reason, Hash(payload));
    }

    private static string NormalizeToken(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Suppression token is required.", parameterName);
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > 256 || normalized.Any(char.IsControl))
            throw new ArgumentException("Suppression token is invalid.", parameterName);
        return normalized;
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
