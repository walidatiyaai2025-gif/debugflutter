using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record IdempotencyRecord(
    string Operation,
    string Token,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string PayloadFingerprint,
    string? ResultFingerprint);

public sealed record IdempotencyDecision(
    bool Allowed,
    bool IsReplay,
    bool IsExpired,
    string Operation,
    string Token,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string PayloadFingerprint,
    string? ResultFingerprint,
    string ReasonCode,
    string Fingerprint);

public static partial class IdempotencyTokenPolicy
{
    public static readonly TimeSpan MinLifetime = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromHours(24);

    [GeneratedRegex("^[a-z0-9][a-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex OperationPattern();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex FingerprintPattern();

    public static IdempotencyDecision Evaluate(
        string operation,
        string token,
        DateTimeOffset issuedAt,
        TimeSpan requestedLifetime,
        string payloadFingerprint,
        DateTimeOffset now,
        IdempotencyRecord? existing = null)
    {
        var normalizedOperation = NormalizeOperation(operation);
        var normalizedToken = NormalizeToken(token);
        var payload = NormalizeFingerprint(payloadFingerprint, nameof(payloadFingerprint));
        var issued = issuedAt.ToUniversalTime();
        var lifetime = ClampLifetime(requestedLifetime);
        var expires = issued.Add(lifetime);
        var nowUtc = now.ToUniversalTime();

        if (existing is not null)
        {
            var existingNormalized = NormalizeExisting(existing);
            if (!string.Equals(existingNormalized.Token, normalizedToken, StringComparison.Ordinal)
                || !string.Equals(existingNormalized.Operation, normalizedOperation, StringComparison.Ordinal))
            {
                return Decision(false, false, false, normalizedOperation, normalizedToken, issued, expires, payload, existingNormalized.ResultFingerprint, "idempotency-scope-conflict");
            }
            if (existingNormalized.ExpiresAtUtc <= nowUtc)
            {
                return Decision(false, false, true, normalizedOperation, normalizedToken, existingNormalized.IssuedAtUtc, existingNormalized.ExpiresAtUtc, payload, existingNormalized.ResultFingerprint, "idempotency-token-expired");
            }
            if (!string.Equals(existingNormalized.PayloadFingerprint, payload, StringComparison.Ordinal))
            {
                return Decision(false, false, false, normalizedOperation, normalizedToken, existingNormalized.IssuedAtUtc, existingNormalized.ExpiresAtUtc, payload, existingNormalized.ResultFingerprint, "idempotency-payload-conflict");
            }
            return Decision(true, true, false, normalizedOperation, normalizedToken, existingNormalized.IssuedAtUtc, existingNormalized.ExpiresAtUtc, payload, existingNormalized.ResultFingerprint, "idempotency-safe-replay");
        }

        if (expires <= nowUtc)
        {
            return Decision(false, false, true, normalizedOperation, normalizedToken, issued, expires, payload, null, "idempotency-token-expired");
        }
        return Decision(true, false, false, normalizedOperation, normalizedToken, issued, expires, payload, null, "idempotency-token-created");
    }

    public static string NormalizeToken(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (!TokenPattern().IsMatch(normalized))
        {
            throw new ArgumentException("Idempotency token is invalid.", nameof(value));
        }
        return normalized;
    }

    private static string NormalizeOperation(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (!OperationPattern().IsMatch(normalized))
        {
            throw new ArgumentException("Operation identity is invalid.", nameof(value));
        }
        return normalized;
    }

    private static TimeSpan ClampLifetime(TimeSpan value)
        => value < MinLifetime ? MinLifetime : value > MaxLifetime ? MaxLifetime : value;

    private static IdempotencyRecord NormalizeExisting(IdempotencyRecord existing)
        => existing with
        {
            Operation = NormalizeOperation(existing.Operation),
            Token = NormalizeToken(existing.Token),
            IssuedAtUtc = existing.IssuedAtUtc.ToUniversalTime(),
            ExpiresAtUtc = existing.ExpiresAtUtc.ToUniversalTime(),
            PayloadFingerprint = NormalizeFingerprint(existing.PayloadFingerprint, nameof(existing.PayloadFingerprint)),
            ResultFingerprint = existing.ResultFingerprint is null ? null : NormalizeFingerprint(existing.ResultFingerprint, nameof(existing.ResultFingerprint))
        };

    private static string NormalizeFingerprint(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (!FingerprintPattern().IsMatch(normalized))
        {
            throw new ArgumentException("Fingerprint must be SHA-256.", parameterName);
        }
        return normalized;
    }

    private static IdempotencyDecision Decision(bool allowed, bool replay, bool expired, string operation, string token, DateTimeOffset issued, DateTimeOffset expires, string payload, string? result, string reason)
    {
        var canonical = $"{operation}|{token}|{issued:O}|{expires:O}|{payload}|{result}|{reason}";
        return new IdempotencyDecision(allowed, replay, expired, operation, token, issued, expires, payload, result, reason, Hash(canonical));
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
