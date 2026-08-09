using System.IO;
using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Flutter.Versioning;

public sealed class FlutterVersionProbe : IFlutterVersionProbe
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(1);
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
            return Empty(
                FlutterVersionProbeStatus.FlutterUnavailable,
                flutterPath,
                "A detected Flutter executable is required before flutter --version can run.");
        }

        var timeout = request.Timeout ?? DefaultTimeout;
        if (timeout <= TimeSpan.Zero)
        {
            return Empty(
                FlutterVersionProbeStatus.InvalidRequest,
                flutterPath,
                "Flutter version probe timeout must be greater than zero.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Empty(
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
            return Empty(
                FlutterVersionProbeStatus.Cancelled,
                flutterPath,
                "Flutter version probe was cancelled.");
        }
        catch (Exception ex)
        {
            return Empty(
                FlutterVersionProbeStatus.Failed,
                flutterPath,
                $"Flutter version probe could not be started: {ex.Message}");
        }

        var parsed = Parse(processResult);
        var status = processResult.Status switch
        {
            ProcessExecutionStatus.Succeeded when parsed.HasRequiredVersionFields
                => FlutterVersionProbeStatus.Succeeded,
            ProcessExecutionStatus.Succeeded
                => FlutterVersionProbeStatus.Partial,
            ProcessExecutionStatus.Cancelled
                => FlutterVersionProbeStatus.Cancelled,
            ProcessExecutionStatus.TimedOut
                => FlutterVersionProbeStatus.TimedOut,
            _ => FlutterVersionProbeStatus.Failed
        };

        var message = status switch
        {
            FlutterVersionProbeStatus.Succeeded
                => "flutter --version completed and required version fields were parsed.",
            FlutterVersionProbeStatus.Partial
                => "flutter --version completed, but one or more required version fields could not be parsed. Raw evidence was preserved.",
            FlutterVersionProbeStatus.Cancelled
                => "Flutter version probe was cancelled. Raw process evidence was preserved.",
            FlutterVersionProbeStatus.TimedOut
                => "Flutter version probe timed out. Raw process evidence was preserved.",
            _ => "flutter --version failed. Raw process evidence was preserved."
        };

        return new FlutterVersionProbeResult(
            status,
            flutterPath,
            parsed.FlutterVersion,
            parsed.Channel,
            parsed.FrameworkRevision,
            parsed.FrameworkDate,
            parsed.EngineRevision,
            parsed.DartVersion,
            parsed.DevToolsVersion,
            parsed.RepositoryUrl,
            message,
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

    private static ParsedVersion Parse(ProcessResult processResult)
    {
        string? flutterVersion = null;
        string? channel = null;
        string? frameworkRevision = null;
        string? frameworkDate = null;
        string? engineRevision = null;
        string? dartVersion = null;
        string? devToolsVersion = null;
        string? repositoryUrl = null;

        foreach (var outputLine in processResult.Output)
        {
            if (outputLine.Stream != ProcessStream.StdOut || string.IsNullOrWhiteSpace(outputLine.Text))
                continue;

            var segments = outputLine.Text
                .Split('•', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                continue;

            var first = segments[0].Trim();
            if (first.StartsWith("Flutter ", StringComparison.OrdinalIgnoreCase))
            {
                flutterVersion ??= ValueAfterPrefix(first, "Flutter ");
                channel ??= FindValue(segments, "channel ");
                repositoryUrl ??= segments.FirstOrDefault(segment =>
                    segment.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    segment.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
                continue;
            }

            if (first.Equals("Framework", StringComparison.OrdinalIgnoreCase))
            {
                var revisionSegment = FindSegment(segments, "revision ");
                frameworkRevision ??= FirstToken(ValueAfterPrefix(revisionSegment, "revision "));
                frameworkDate ??= FindFrameworkDate(segments, revisionSegment);
                continue;
            }

            if (first.Equals("Engine", StringComparison.OrdinalIgnoreCase))
            {
                var revision = FindValue(segments, "revision ");
                var hash = FindValue(segments, "hash ");
                engineRevision ??= FirstToken(revision) ?? FirstToken(hash);
                continue;
            }

            if (first.Equals("Tools", StringComparison.OrdinalIgnoreCase))
            {
                dartVersion ??= FirstToken(FindValue(segments, "Dart "));
                devToolsVersion ??= FirstToken(FindValue(segments, "DevTools "));
            }
        }

        return new ParsedVersion(
            flutterVersion,
            channel,
            frameworkRevision,
            frameworkDate,
            engineRevision,
            dartVersion,
            devToolsVersion,
            repositoryUrl);
    }

    private static string? FindSegment(IEnumerable<string> segments, string prefix)
        => segments.FirstOrDefault(segment =>
            segment.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static string? FindValue(IEnumerable<string> segments, string prefix)
        => ValueAfterPrefix(FindSegment(segments, prefix), prefix);

    private static string? ValueAfterPrefix(string? value, string prefix)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var result = value[prefix.Length..].Trim();
        return result.Length == 0 ? null : result;
    }

    private static string? FirstToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var token = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    private static string? FindFrameworkDate(IReadOnlyList<string> segments, string? revisionSegment)
    {
        if (revisionSegment is null)
            return null;

        var revisionIndex = -1;
        for (var index = 0; index < segments.Count; index++)
        {
            if (ReferenceEquals(segments[index], revisionSegment) ||
                string.Equals(segments[index], revisionSegment, StringComparison.Ordinal))
            {
                revisionIndex = index;
                break;
            }
        }

        if (revisionIndex < 0 || revisionIndex + 1 >= segments.Count)
            return null;

        var candidate = segments[revisionIndex + 1].Trim();
        return candidate.Length == 0 ? null : candidate;
    }

    private static FlutterVersionProbeResult Empty(
        FlutterVersionProbeStatus status,
        string? flutterPath,
        string message)
        => new(
            status,
            flutterPath,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            message);

    private sealed record ParsedVersion(
        string? FlutterVersion,
        string? Channel,
        string? FrameworkRevision,
        string? FrameworkDate,
        string? EngineRevision,
        string? DartVersion,
        string? DevToolsVersion,
        string? RepositoryUrl)
    {
        public bool HasRequiredVersionFields
            => !string.IsNullOrWhiteSpace(FlutterVersion) &&
               !string.IsNullOrWhiteSpace(Channel) &&
               !string.IsNullOrWhiteSpace(FrameworkRevision) &&
               !string.IsNullOrWhiteSpace(DartVersion);
    }
}
