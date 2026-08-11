using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Logging;

public enum LogSignalSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2
}

public sealed record LogSignal(
    string Key,
    LogSignalSeverity Severity,
    string? Code,
    string Message,
    int Occurrences);

public sealed record LogSignalExtractionResult(
    IReadOnlyList<LogSignal> Signals,
    int RetainedLineCount,
    string Fingerprint);

public static partial class LogSignalExtractor
{
    public const int DefaultMaxLines = 5000;
    public const int MaxLines = 20000;

    public static LogSignalExtractionResult Extract(string? log, int maxLines = DefaultMaxLines)
    {
        var normalized = NormalizeLineEndings(log ?? string.Empty);
        var boundedMaxLines = Math.Clamp(maxLines, 1, MaxLines);
        var lines = normalized.Split('\n').Take(boundedMaxLines).ToArray();
        var extracted = new List<LogSignal>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var gradle = GradleTaskRegex().Match(line);
            if (gradle.Success)
            {
                extracted.Add(new LogSignal(
                    "gradle-task-failure",
                    LogSignalSeverity.Error,
                    gradle.Groups["task"].Value,
                    line,
                    1));
                continue;
            }

            var analyzer = AnalyzerCodeRegex().Match(line);
            if (analyzer.Success)
            {
                var severity = ClassifySeverity(line);
                extracted.Add(new LogSignal(
                    "flutter-analyzer",
                    severity,
                    analyzer.Groups["code"].Value.ToLowerInvariant(),
                    line,
                    1));
                continue;
            }

            var classified = ClassifySeverity(line);
            if (classified == LogSignalSeverity.Error)
            {
                extracted.Add(new LogSignal("generic-error", classified, null, line, 1));
            }
            else if (classified == LogSignalSeverity.Warning)
            {
                extracted.Add(new LogSignal("generic-warning", classified, null, line, 1));
            }
        }

        var collapsed = extracted
            .GroupBy(signal => $"{signal.Key}|{signal.Code ?? string.Empty}|{NormalizeMessage(signal.Message)}", StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                return first with { Occurrences = group.Count() };
            })
            .OrderByDescending(signal => signal.Severity)
            .ThenBy(signal => signal.Key, StringComparer.Ordinal)
            .ThenBy(signal => signal.Code ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(signal => signal.Message, StringComparer.Ordinal)
            .ToArray();

        var fingerprint = ComputeFingerprint(collapsed, lines.Length);
        return new LogSignalExtractionResult(collapsed, lines.Length, fingerprint);
    }

    public static string NormalizeLineEndings(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    public static LogSignalSeverity ClassifySeverity(string line)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(line);
        if (line.Contains("error", StringComparison.OrdinalIgnoreCase)
            || line.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || line.Contains("exception", StringComparison.OrdinalIgnoreCase))
        {
            return LogSignalSeverity.Error;
        }

        if (line.Contains("warning", StringComparison.OrdinalIgnoreCase)
            || line.Contains("warn:", StringComparison.OrdinalIgnoreCase))
        {
            return LogSignalSeverity.Warning;
        }

        return LogSignalSeverity.Info;
    }

    private static string NormalizeMessage(string message) => WhitespaceRegex().Replace(message.Trim(), " ");

    private static string ComputeFingerprint(IEnumerable<LogSignal> signals, int retainedLineCount)
    {
        var canonicalSignals = signals.Select(signal => string.Join(':',
            signal.Severity,
            signal.Key,
            signal.Code ?? string.Empty,
            NormalizeMessage(signal.Message),
            signal.Occurrences));
        var canonical = string.Join('|', canonicalSignals.Prepend(retainedLineCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    [GeneratedRegex("Execution failed for task ['\"](?<task>:[^'\"]+)['\"]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GradleTaskRegex();

    [GeneratedRegex("\\s-\\s(?<code>[a-z][a-z0-9_]+)\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AnalyzerCodeRegex();

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
