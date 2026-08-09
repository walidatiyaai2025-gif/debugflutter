using System.IO;
using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Flutter.Detection;

public sealed class FlutterVersionProbe : IFlutterVersionProbe
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(1);
    private static readonly char[] MetadataSeparators = ['•', '\a'];
    private static readonly string[] MetadataSeparatorAliases = ["ΓÇó", "â€¢"];
    private readonly IProcessRunner _processRunner;

    public FlutterVersionProbe(IProcessRunner processRunner)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public async Task<FlutterVersionProbeResult> ProbeAsync(
        FlutterVersionProbeRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Flutter);

        var flutterPath = request.Flutter.FlutterPath;
        if (!request.Flutter.Installed || string.IsNullOrWhiteSpace(flutterPath))
        {
            return Result(
                FlutterVersionProbeStatus.FlutterUnavailable,
                flutterPath,
                "A detected Flutter executable is required before flutter --version can run.");
        }

        var timeout = request.Timeout ?? DefaultTimeout;
        if (timeout <= TimeSpan.Zero)
        {
            return Result(
                FlutterVersionProbeStatus.InvalidRequest,
                flutterPath,
                "Flutter version probe timeout must be greater than zero.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Result(
                FlutterVersionProbeStatus.Cancelled,
                flutterPath,
                "Flutter version probe was cancelled before it started.");
        }

        ProcessResult processResult;
        try
        {
            processResult = await _processRunner.RunAsync(
                BuildProcessRequest(flutterPath, timeout),
                progress,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Result(
                FlutterVersionProbeStatus.Cancelled,
                flutterPath,
                "Flutter version probe was cancelled.");
        }
        catch (Exception ex)
        {
            return Result(
                FlutterVersionProbeStatus.ProbeFailed,
                flutterPath,
                $"Flutter version probe could not be started: {ex.Message}");
        }

        if (processResult.Status == ProcessExecutionStatus.Cancelled)
        {
            return Result(
                FlutterVersionProbeStatus.Cancelled,
                flutterPath,
                "Flutter version probe was cancelled.",
                processResult: processResult);
        }

        if (processResult.Status == ProcessExecutionStatus.TimedOut)
        {
            return Result(
                FlutterVersionProbeStatus.TimedOut,
                flutterPath,
                "Flutter version probe timed out.",
                processResult: processResult);
        }

        if (!processResult.IsSuccess)
        {
            return Result(
                FlutterVersionProbeStatus.ProbeFailed,
                flutterPath,
                "flutter --version failed. Raw process evidence was preserved.",
                processResult: processResult);
        }

        var parsed = Parse(processResult.Output);
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(parsed.FlutterVersion)) missing.Add("Flutter version");
        if (string.IsNullOrWhiteSpace(parsed.Channel)) missing.Add("channel");
        if (string.IsNullOrWhiteSpace(parsed.FrameworkRevision)) missing.Add("framework revision");
        if (string.IsNullOrWhiteSpace(parsed.DartVersion)) missing.Add("Dart version");

        if (missing.Count > 0)
        {
            return Result(
                FlutterVersionProbeStatus.ParseFailed,
                flutterPath,
                $"flutter --version completed, but required fields could not be parsed: {string.Join(", ", missing)}. Raw process evidence was preserved.",
                parsed,
                processResult);
        }

        return Result(
            FlutterVersionProbeStatus.Succeeded,
            flutterPath,
            "flutter --version completed and structured version data was parsed.",
            parsed,
            processResult);
    }

    private static ProcessRequest BuildProcessRequest(string flutterPath, TimeSpan timeout)
    {
        var extension = Path.GetExtension(flutterPath);
        if (extension.Equals(".bat", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            return new ProcessRequest(
                "cmd.exe",
                new[] { "/d", "/v:off", "/c", flutterPath, "--version" },
                Timeout: timeout,
                DisplayName: "Flutter --version");
        }

        return new ProcessRequest(
            flutterPath,
            new[] { "--version" },
            Timeout: timeout,
            DisplayName: "Flutter --version");
    }

    private static ParsedVersion Parse(IReadOnlyList<ProcessOutputLine> output)
    {
        string? flutterVersion = null;
        string? channel = null;
        string? repositoryUrl = null;
        string? frameworkRevision = null;
        string? frameworkDate = null;
        string? engineHash = null;
        string? engineRevision = null;
        string? engineDate = null;
        string? dartVersion = null;
        string? devToolsVersion = null;

        foreach (var outputLine in output)
        {
            if (outputLine.Stream != ProcessStream.StdOut)
                continue;

            var line = outputLine.Text.Trim();
            if (line.Length == 0)
                continue;

            // Windows command shims can round-trip U+2022 BULLET through several
            // OEM/ANSI representations. Normalize only known separator aliases for
            // parsing; ProcessResult retains the exact original text as raw evidence.
            var normalizedLine = NormalizeMetadataSeparators(line);
            var segments = normalizedLine.Split(
                MetadataSeparators,
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                continue;

            if (segments[0].StartsWith("Flutter ", StringComparison.OrdinalIgnoreCase))
            {
                flutterVersion ??= ValueAfterPrefix(segments[0], "Flutter ");
                channel ??= FindPrefixedValue(segments, "channel ");
                repositoryUrl ??= segments.FirstOrDefault(IsRepositoryUrl);
                continue;
            }

            if (segments[0].Equals("Framework", StringComparison.OrdinalIgnoreCase))
            {
                frameworkRevision ??= FindPrefixedValue(segments, "revision ");
                frameworkDate ??= LastMetadataSegment(segments, "revision ");
                continue;
            }

            if (segments[0].Equals("Engine", StringComparison.OrdinalIgnoreCase))
            {
                engineHash ??= FindPrefixedValue(segments, "hash ");
                engineRevision ??= FindPrefixedValue(segments, "revision ") ??
                                   FindParenthesizedPrefixedValue(segments, "revision ");
                engineDate ??= LastMetadataSegment(segments, "hash ", "revision ");
                continue;
            }

            if (segments[0].Equals("Tools", StringComparison.OrdinalIgnoreCase))
            {
                dartVersion ??= FindPrefixedValue(segments, "Dart ");
                devToolsVersion ??= FindPrefixedValue(segments, "DevTools ");
            }
        }

        return new ParsedVersion(
            flutterVersion,
            channel,
            repositoryUrl,
            frameworkRevision,
            frameworkDate,
            engineHash,
            engineRevision,
            engineDate,
            dartVersion,
            devToolsVersion);
    }

    private static string NormalizeMetadataSeparators(string line)
    {
        foreach (var alias in MetadataSeparatorAliases)
            line = line.Replace(alias, "•", StringComparison.Ordinal);

        return line;
    }

    private static bool IsRepositoryUrl(string segment)
        => Uri.TryCreate(segment, UriKind.Absolute, out var uri) &&
           (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase));

    private static string? LastMetadataSegment(string[] segments, params string[] descriptorPrefixes)
    {
        if (segments.Length < 3)
            return null;

        var candidate = segments[^1].Trim();
        if (candidate.Length == 0 || descriptorPrefixes.Any(prefix => candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            return null;

        return candidate;
    }

    private static string? FindPrefixedValue(IEnumerable<string> segments, string prefix)
    {
        foreach (var segment in segments)
        {
            var value = ValueAfterPrefix(segment, prefix);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static string? FindParenthesizedPrefixedValue(IEnumerable<string> segments, string prefix)
    {
        foreach (var segment in segments)
        {
            var searchFrom = 0;
            while (searchFrom < segment.Length)
            {
                var open = segment.IndexOf('(', searchFrom);
                if (open < 0)
                    break;

                var close = segment.IndexOf(')', open + 1);
                if (close < 0)
                    break;

                var inner = segment[(open + 1)..close].Trim();
                var value = ValueAfterPrefix(inner, prefix);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;

                searchFrom = close + 1;
            }
        }

        return null;
    }

    private static string? ValueAfterPrefix(string value, string prefix)
    {
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var result = value[prefix.Length..].Trim();
        if (result.Length == 0)
            return null;

        var parenthesis = result.IndexOf(" (", StringComparison.Ordinal);
        return parenthesis > 0 ? result[..parenthesis].Trim() : result;
    }

    private static FlutterVersionProbeResult Result(
        FlutterVersionProbeStatus status,
        string? flutterPath,
        string message,
        ParsedVersion? parsed = null,
        ProcessResult? processResult = null)
        => new(
            status,
            flutterPath,
            parsed?.FlutterVersion,
            parsed?.Channel,
            parsed?.RepositoryUrl,
            parsed?.FrameworkRevision,
            parsed?.FrameworkDate,
            parsed?.EngineHash,
            parsed?.EngineRevision,
            parsed?.EngineDate,
            parsed?.DartVersion,
            parsed?.DevToolsVersion,
            message,
            processResult);

    private sealed record ParsedVersion(
        string? FlutterVersion,
        string? Channel,
        string? RepositoryUrl,
        string? FrameworkRevision,
        string? FrameworkDate,
        string? EngineHash,
        string? EngineRevision,
        string? EngineDate,
        string? DartVersion,
        string? DevToolsVersion);
}
