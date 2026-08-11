using System.Security.Cryptography;
using System.Text;

namespace FlutterBuildDoctor.Application.Processes;

public enum RetryFailureKind
{
    Transient,
    Permanent,
    Cancelled
}

public sealed record RetryBackoffDecision(
    int RetryCount,
    TimeSpan BaseDelay,
    TimeSpan MaxDelay,
    IReadOnlyList<TimeSpan> Schedule,
    bool RetryAllowed,
    string ReasonCode,
    string Fingerprint);

public static class RetryBackoffPolicy
{
    public const int MaxRetryCount = 5;
    public static readonly TimeSpan MinBaseDelay = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan MaxBaseDelay = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaxSupportedDelay = TimeSpan.FromMinutes(5);

    public static RetryBackoffDecision Build(
        int retryCount,
        TimeSpan baseDelay,
        TimeSpan maxDelay,
        RetryFailureKind failureKind)
    {
        var boundedRetries = Math.Clamp(retryCount, 0, MaxRetryCount);
        var boundedBase = Clamp(baseDelay, MinBaseDelay, MaxBaseDelay);
        var boundedMax = Clamp(maxDelay, boundedBase, MaxSupportedDelay);

        var allowed = failureKind == RetryFailureKind.Transient && boundedRetries > 0;
        var reason = failureKind switch
        {
            RetryFailureKind.Cancelled => "cancelled-no-retry",
            RetryFailureKind.Permanent => "permanent-no-retry",
            RetryFailureKind.Transient when boundedRetries == 0 => "retry-budget-zero",
            _ => "transient-retry"
        };

        var schedule = new List<TimeSpan>();
        if (allowed)
        {
            for (var attempt = 0; attempt < boundedRetries; attempt++)
            {
                var multiplier = 1L << attempt;
                var ticks = Math.Min(boundedMax.Ticks, boundedBase.Ticks * multiplier);
                schedule.Add(TimeSpan.FromTicks(ticks));
            }
        }

        var canonical = string.Join('|', boundedRetries, boundedBase.Ticks, boundedMax.Ticks, failureKind, reason,
            string.Join(',', schedule.Select(delay => delay.Ticks)));
        return new RetryBackoffDecision(boundedRetries, boundedBase, boundedMax, schedule, allowed, reason, Hash(canonical));
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max)
    {
        if (value < min)
        {
            return min;
        }
        if (value > max)
        {
            return max;
        }
        return value;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
