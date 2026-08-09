using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Flutter.Detection;

namespace FlutterBuildDoctor.Flutter.Versioning;

public enum FlutterVersionProbeStatus
{
    Succeeded = 0,
    Partial,
    FlutterUnavailable,
    InvalidRequest,
    Failed,
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
    string? FrameworkDate,
    string? EngineRevision,
    string? DartVersion,
    string? DevToolsVersion,
    string? RepositoryUrl,
    string Message,
    ProcessResult? ProcessResult = null)
{
    public bool IsSuccess => Status == FlutterVersionProbeStatus.Succeeded;

    public bool HasRequiredVersionFields
        => !string.IsNullOrWhiteSpace(FlutterVersion) &&
           !string.IsNullOrWhiteSpace(Channel) &&
           !string.IsNullOrWhiteSpace(FrameworkRevision) &&
           !string.IsNullOrWhiteSpace(DartVersion);
}

public interface IFlutterVersionProbe
{
    Task<FlutterVersionProbeResult> ProbeAsync(
        FlutterVersionProbeRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default);
}
