using System;
using System.Security.Cryptography;
using System.Text;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record NetworkRetryBudgetDecision(
    string EndpointIdentity,
    int AttemptNumber,
    int MaxAttempts,
    TimeSpan BaseDelay,
    TimeSpan MaxDelay,
    TimeSpan Elapsed,
    TimeSpan NextDelay,
    bool Exhausted,
    string ReasonCode,
    string Fingerprint);

public static class NetworkRetryBudgetPolicy
{
    public static readonly TimeSpan MinBaseDelay = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan MaxBaseDelay = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan AbsoluteMaxDelay = TimeSpan.FromMinutes(5);

    public static NetworkRetryBudgetDecision Evaluate(
        string endpointIdentity,
        int attemptNumber,
        int maxAttempts,
        TimeSpan baseDelay,
        TimeSpan maxDelay,
        TimeSpan elapsed,
        TimeSpan? retryAfter = null)
    {
        var endpoint = NormalizeEndpoint(endpointIdentity);
        if (attemptNumber < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNumber));
        }

        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        }

        if (retryAfter.HasValue && retryAfter.Value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retryAfter));
        }

        var normalizedMaxAttempts = Math.Clamp(maxAttempts, 1, 10);
        var normalizedBase = Clamp(baseDelay, MinBaseDelay, MaxBaseDelay);
        var normalizedMax = Clamp(maxDelay, normalizedBase, AbsoluteMaxDelay);
        var exhausted = attemptNumber >= normalizedMaxAttempts;
        var next = exhausted ? TimeSpan.Zero : ComputeBackoff(attemptNumber, normalizedBase, normalizedMax);
        if (!exhausted && retryAfter.HasValue && retryAfter.Value > next)
        {
            next = retryAfter.Value > normalizedMax ? normalizedMax : retryAfter.Value;
        }

        var reason = exhausted ? "network-retry-budget-exhausted" : "network-retry-budget-available";
        var canonical = $"{endpoint}|{attemptNumber}|{normalizedMaxAttempts}|{normalizedBase.Ticks}|{normalizedMax.Ticks}|{elapsed.Ticks}|{next.Ticks}|{exhausted}|{reason}";
        return new NetworkRetryBudgetDecision(
            endpoint,
            attemptNumber,
            normalizedMaxAttempts,
            normalizedBase,
            normalizedMax,
            elapsed,
            next,
            exhausted,
            reason,
            Hash(canonical));
    }

    private static string NormalizeEndpoint(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Endpoint identity is required.", nameof(value));
        }

        var normalized = value.Trim();
        if (normalized.Length > 256 || normalized.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
        {
            throw new ArgumentException("Endpoint identity is invalid.", nameof(value));
        }

        if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            var builder = new UriBuilder(uri)
            {
                Scheme = uri.Scheme.ToLowerInvariant(),
                Host = uri.Host.ToLowerInvariant()
            };
            normalized = builder.Uri.AbsoluteUri.TrimEnd('/');
        }
        else
        {
            normalized = normalized.ToLowerInvariant();
        }

        return normalized;
    }

    private static TimeSpan ComputeBackoff(int attemptNumber, TimeSpan baseDelay, TimeSpan maxDelay)
    {
        var exponent = Math.Clamp(Math.Max(0, attemptNumber - 1), 0, 20);
        var multiplier = 1L << exponent;
        var ticks = baseDelay.Ticks > maxDelay.Ticks / multiplier
            ? maxDelay.Ticks
            : baseDelay.Ticks * multiplier;
        return TimeSpan.FromTicks(Math.Min(ticks, maxDelay.Ticks));
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
