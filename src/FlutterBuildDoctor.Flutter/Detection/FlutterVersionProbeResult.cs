using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Flutter.Detection;

public enum FlutterVersionProbeStatus
{
    Succeeded = 0,
    FlutterUnavailable,
    InvalidRequest,
    ProbeFailed,
    ParseFailed,
    Cancelled,
    TimedOut
}

public sealed record FlutterVersionProbeRequest(
    FlutterDetectionResult Flutter,
    TimeSpan? Timeout = null);

public sealed record FlutterVersionProbeResult(
    FlutterVersionProbeStatus Status,
    string? FlutterPath,
    string? FlutterVersion,
    string? Channel,
    string? FrameworkRevision,
    string? DartVersion,
    string? EngineRevision,
    string? DevToolsVersion,
    string Message,
    ProcessResult? ProcessResult = null)
{
    public bool IsSuccess => Status == FlutterVersionProbeStatus.Succeeded;
}

public interface IFlutterVersionProbe
{
    Task<FlutterVersionProbeResult> ProbeAsync(
        FlutterVersionProbeRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default);
}
