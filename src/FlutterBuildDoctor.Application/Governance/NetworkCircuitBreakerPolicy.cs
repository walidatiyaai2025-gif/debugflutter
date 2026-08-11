using System.Security.Cryptography;
using System.Text;

namespace FlutterBuildDoctor.Application.Governance;

public enum CircuitState
{
    Closed,
    Open,
    HalfOpen
}

public sealed record CircuitObservation(bool Success, DateTimeOffset ObservedAtUtc);

public sealed record CircuitDecision(
    string Endpoint,
    CircuitState State,
    int FailureThreshold,
    TimeSpan OpenDuration,
    int ConsecutiveFailures,
    string ReasonCode,
    string Fingerprint);

public static class NetworkCircuitBreakerPolicy
{
    public static CircuitDecision Evaluate(
        string endpoint,
        IEnumerable<CircuitObservation> observations,
        int failureThreshold,
        TimeSpan openDuration,
        DateTimeOffset now)
    {
        var normalizedEndpoint = NormalizeEndpoint(endpoint);
        ArgumentNullException.ThrowIfNull(observations);
        var threshold = Math.Clamp(failureThreshold, 1, 20);
        var duration = TimeSpan.FromSeconds(Math.Clamp(openDuration.TotalSeconds, 5, 900));
        var nowUtc = now.ToUniversalTime();
        var ordered = observations
            .Select(item => item with { ObservedAtUtc = item.ObservedAtUtc.ToUniversalTime() })
            .Where(item => item.ObservedAtUtc <= nowUtc)
            .OrderBy(item => item.ObservedAtUtc)
            .ToArray();

        var failures = 0;
        DateTimeOffset? latestFailure = null;
        for (var index = ordered.Length - 1; index >= 0; index--)
        {
            if (ordered[index].Success)
                break;
            failures++;
            latestFailure ??= ordered[index].ObservedAtUtc;
        }

        CircuitState state;
        string reason;
        if (failures < threshold)
        {
            state = CircuitState.Closed;
            reason = "circuit-closed";
        }
        else if (latestFailure.HasValue && nowUtc - latestFailure.Value >= duration)
        {
            state = CircuitState.HalfOpen;
            reason = "circuit-half-open";
        }
        else
        {
            state = CircuitState.Open;
            reason = "circuit-open";
        }

        var payload = $"{normalizedEndpoint}|{state}|{threshold}|{duration.TotalSeconds:0}|{failures}";
        return new CircuitDecision(normalizedEndpoint, state, threshold, duration, failures, reason, Hash(payload));
    }

    public static string NormalizeEndpoint(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Endpoint identity is required.", nameof(value));
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > 128 || normalized.Any(char.IsControl))
            throw new ArgumentException("Endpoint identity is invalid.", nameof(value));
        return normalized;
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
