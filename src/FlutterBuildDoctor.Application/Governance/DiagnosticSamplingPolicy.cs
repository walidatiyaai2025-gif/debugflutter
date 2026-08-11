using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

public enum DiagnosticSampleSeverity
{
    Information = 0,
    Warning = 1,
    Critical = 2
}

public sealed record DiagnosticSample(
    string Category,
    string Message,
    DiagnosticSampleSeverity Severity,
    DateTimeOffset ObservedAt);

public sealed record DiagnosticSamplingDecision(
    int MaxSamples,
    TimeSpan Interval,
    IReadOnlyList<DiagnosticSample> Samples,
    string ReasonCode,
    string Fingerprint);

public static partial class DiagnosticSamplingPolicy
{
    public const int MaxRetainedSamples = 500;
    public static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan MaxInterval = TimeSpan.FromMinutes(10);

    public static DiagnosticSamplingDecision Apply(IEnumerable<DiagnosticSample> samples, int maxSamples, TimeSpan interval)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var limit = Math.Clamp(maxSamples, 1, MaxRetainedSamples);
        var boundedInterval = TimeSpan.FromMilliseconds(Math.Clamp(interval.TotalMilliseconds, MinInterval.TotalMilliseconds, MaxInterval.TotalMilliseconds));
        var normalized = samples.Select(Normalize)
            .OrderBy(item => item.ObservedAt)
            .ThenBy(item => item.Category, StringComparer.Ordinal)
            .ThenBy(item => item.Message, StringComparer.Ordinal)
            .ToArray();

        var critical = normalized.Where(item => item.Severity == DiagnosticSampleSeverity.Critical).ToList();
        var retained = new List<DiagnosticSample>(critical);
        var seenBuckets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sample in normalized.Where(item => item.Severity != DiagnosticSampleSeverity.Critical))
        {
            var bucket = sample.ObservedAt.UtcTicks / Math.Max(1, boundedInterval.Ticks);
            var key = $"{sample.Category}|{sample.Severity}|{sample.Message}|{bucket}";
            if (!seenBuckets.Add(key)) continue;
            if (retained.Count >= limit) break;
            retained.Add(sample);
        }

        var ordered = retained
            .OrderBy(item => item.ObservedAt)
            .ThenByDescending(item => item.Severity)
            .ThenBy(item => item.Category, StringComparer.Ordinal)
            .ThenBy(item => item.Message, StringComparer.Ordinal)
            .ToArray();
        var reason = normalized.Length == ordered.Length ? "samples-preserved" : "samples-downsampled";
        var canonical = $"{limit}|{boundedInterval.Ticks}|{reason}\n" +
            string.Join('\n', ordered.Select(item => $"{item.ObservedAt:O}|{item.Severity}|{item.Category}|{item.Message}"));
        return new DiagnosticSamplingDecision(limit, boundedInterval, ordered, reason, Hash(canonical));
    }

    public static DiagnosticSample Normalize(DiagnosticSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        var category = NormalizeCategory(sample.Category);
        ArgumentException.ThrowIfNullOrWhiteSpace(sample.Message);
        if (sample.Message.Any(char.IsControl)) throw new ArgumentException("Diagnostic message contains control characters.", nameof(sample));
        var message = sample.Message.Trim();
        if (message.Length > 2048) message = message[..2048];
        if (!Enum.IsDefined(sample.Severity)) throw new ArgumentOutOfRangeException(nameof(sample), "Diagnostic severity is invalid.");
        return sample with { Category = category, Message = message, ObservedAt = sample.ObservedAt.ToUniversalTime() };
    }

    public static string NormalizeCategory(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (!CategoryRegex().IsMatch(normalized)) throw new ArgumentException("Diagnostic category is invalid.", nameof(value));
        return normalized;
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex CategoryRegex();
}
