using System.Security.Cryptography;
using System.Text;

namespace FlutterBuildDoctor.Application.Governance;

public enum RetainedOutputStream
{
    Stdout = 0,
    Stderr = 1
}

public sealed record ProcessOutputLine(RetainedOutputStream Stream, DateTimeOffset Timestamp, string Text);

public sealed record RetainedOutputLine(RetainedOutputStream Stream, DateTimeOffset TimestampUtc, string Text, int Utf8Bytes);

public sealed record ProcessOutputRetentionDecision(
    IReadOnlyList<RetainedOutputLine> Lines,
    int RetainedBytes,
    bool Truncated,
    string ReasonCode,
    string Fingerprint);

public static class ProcessOutputRetentionPolicy
{
    public const int MaxInputLines = 10_000;
    public const int MaxRetainedLines = 2_000;
    public const int MaxRetainedBytes = 1_048_576;
    public const int MaxLineCharacters = 4_096;

    private static readonly string[] SecretMarkers =
    [
        "password=", "passwd=", "token=", "secret=", "api_key=", "apikey=", "authorization:"
    ];

    public static ProcessOutputRetentionDecision Evaluate(
        IEnumerable<ProcessOutputLine> lines,
        int requestedLineLimit = MaxRetainedLines,
        int requestedByteLimit = MaxRetainedBytes)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var input = lines.ToArray();
        if (input.Length > MaxInputLines)
        {
            throw new ArgumentOutOfRangeException(nameof(lines), "Process output exceeds the input bound.");
        }

        var lineLimit = Math.Clamp(requestedLineLimit, 1, MaxRetainedLines);
        var byteLimit = Math.Clamp(requestedByteLimit, 1_024, MaxRetainedBytes);

        var normalized = input.Select(NormalizeLine)
            .OrderByDescending(line => line.Stream == RetainedOutputStream.Stderr)
            .ThenBy(line => line.TimestampUtc)
            .ThenBy(line => line.Text, StringComparer.Ordinal)
            .ToArray();

        var retained = new List<RetainedOutputLine>(Math.Min(lineLimit, normalized.Length));
        var retainedBytes = 0;
        foreach (var line in normalized)
        {
            if (retained.Count >= lineLimit || retainedBytes + line.Utf8Bytes > byteLimit)
            {
                continue;
            }
            retained.Add(line);
            retainedBytes += line.Utf8Bytes;
        }

        var truncated = retained.Count != normalized.Length;
        var reason = truncated ? "output-retained-truncated" : "output-retained";
        var canonical = string.Join('\n', retained.Select(line => $"{(int)line.Stream}|{line.TimestampUtc:O}|{line.Text}"));
        var fingerprint = Hash($"{reason}|{retainedBytes}|{canonical}");
        return new ProcessOutputRetentionDecision(retained, retainedBytes, truncated, reason, fingerprint);
    }

    private static RetainedOutputLine NormalizeLine(ProcessOutputLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (!Enum.IsDefined(line.Stream))
        {
            throw new ArgumentOutOfRangeException(nameof(line), "Output stream is invalid.");
        }

        var timestamp = line.Timestamp.ToUniversalTime();
        var text = line.Text ?? string.Empty;
        if (text.Any(char.IsControl) && text.Any(ch => ch is not '\r' and not '\n' and not '\t'))
        {
            text = string.Concat(text.Select(ch => char.IsControl(ch) && ch is not '\r' and not '\n' and not '\t' ? '�' : ch));
        }

        text = Redact(text);
        if (text.Length > MaxLineCharacters)
        {
            text = text[..(MaxLineCharacters - 1)] + "…";
        }

        return new RetainedOutputLine(line.Stream, timestamp, text, Encoding.UTF8.GetByteCount(text));
    }

    private static string Redact(string value)
    {
        var lowered = value.ToLowerInvariant();
        return SecretMarkers.Any(lowered.Contains) ? "[REDACTED]" : value;
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
