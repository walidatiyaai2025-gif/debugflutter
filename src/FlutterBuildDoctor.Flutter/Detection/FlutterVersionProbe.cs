using System.Text.RegularExpressions;
using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Flutter.Detection;

public enum FlutterVersionProbeStatus
{
    Succeeded = 0,
    Failed,
    Cancelled,
    TimedOut,
    ParseFailed
}

public sealed record FlutterVersionInfo(
    string FlutterVersion,
    string Channel,
    string? FrameworkRevision,
    string? EngineRevision,
    string? DartVersion,
    string? DevToolsVersion);

public sealed record FlutterVersionProbeResult(
    FlutterVersionProbeStatus Status,
    FlutterVersionInfo? Version,
    string RawOutput,
    string? Message,
    ProcessResult ProcessResult)
{
    public bool IsSuccess => Status == FlutterVersionProbeStatus.Succeeded;
}

public interface IFlutterVersionProbe
{
    Task<FlutterVersionProbeResult> ProbeAsync(
        string flutterExecutable,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default);
}

public sealed class FlutterVersionProbe : IFlutterVersionProbe
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private readonly IProcessRunner _processRunner;

    public FlutterVersionProbe(IProcessRunner processRunner)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public async Task<FlutterVersionProbeResult> ProbeAsync(
        string flutterExecutable,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flutterExecutable);

        var request = new ProcessRequest(
            flutterExecutable,
            new[] { "--version" },
            workingDirectory,
            Timeout: DefaultTimeout,
            DisplayName: "flutter --version");

        var result = await _processRunner.RunAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);
        var rawOutput = string.Join(Environment.NewLine, result.Output.Select(static line => line.Text));

        if (result.Status != ProcessExecutionStatus.Succeeded)
        {
            return new FlutterVersionProbeResult(
                MapStatus(result.Status),
                null,
                rawOutput,
                result.FailureReason ?? $"flutter --version ended with status {result.Status}.",
                result);
        }

        if (!TryParse(rawOutput, out var version, out var message))
        {
            return new FlutterVersionProbeResult(
                FlutterVersionProbeStatus.ParseFailed,
                null,
                rawOutput,
                message,
                result);
        }

        return new FlutterVersionProbeResult(
            FlutterVersionProbeStatus.Succeeded,
            version,
            rawOutput,
            message,
            result);
    }

    private static bool TryParse(string rawOutput, out FlutterVersionInfo? version, out string message)
    {
        version = null;
        var header = Regex.Match(
            rawOutput,
            @"(?im)^Flutter\s+(?<version>[^\s•]+)\s+•\s+channel\s+(?<channel>[^•\r\n]+)",
            RegexOptions.CultureInvariant);

        if (!header.Success)
        {
            message = "Could not parse Flutter version and channel from flutter --version output.";
            return false;
        }

        var flutterVersion = header.Groups["version"].Value.Trim();
        var channel = header.Groups["channel"].Value.Trim();
        var frameworkRevision = ExtractRevision(rawOutput, "Framework");
        var engineRevision = ExtractRevision(rawOutput, "Engine") ?? ExtractHash(rawOutput, "Engine");
        var dartVersion = ExtractToolVersion(rawOutput, "Dart");
        var devToolsVersion = ExtractToolVersion(rawOutput, "DevTools");

        version = new FlutterVersionInfo(
            flutterVersion,
            channel,
            frameworkRevision,
            engineRevision,
            dartVersion,
            devToolsVersion);

        var missing = new List<string>();
        if (frameworkRevision is null) missing.Add("framework revision");
        if (engineRevision is null) missing.Add("engine revision/hash");
        if (dartVersion is null) missing.Add("Dart version");

        message = missing.Count == 0
            ? "Flutter version output parsed successfully."
            : $"Flutter version/channel parsed; optional fields missing: {string.Join(", ", missing)}.";
        return true;
    }

    private static string? ExtractRevision(string rawOutput, string section)
    {
        var match = Regex.Match(
            rawOutput,
            $@"(?im)^{Regex.Escape(section)}\s+•.*?\brevision\s+(?<value>[^\s•]+)",
            RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    private static string? ExtractHash(string rawOutput, string section)
    {
        var match = Regex.Match(
            rawOutput,
            $@"(?im)^{Regex.Escape(section)}\s+•.*?\bhash\s+(?<value>[^\s•]+)",
            RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    private static string? ExtractToolVersion(string rawOutput, string tool)
    {
        var match = Regex.Match(
            rawOutput,
            $@"(?im)\b{Regex.Escape(tool)}\s+(?<value>[^\s•]+)",
            RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    private static FlutterVersionProbeStatus MapStatus(ProcessExecutionStatus status)
        => status switch
        {
            ProcessExecutionStatus.Cancelled => FlutterVersionProbeStatus.Cancelled,
            ProcessExecutionStatus.TimedOut => FlutterVersionProbeStatus.TimedOut,
            _ => FlutterVersionProbeStatus.Failed
        };
}
