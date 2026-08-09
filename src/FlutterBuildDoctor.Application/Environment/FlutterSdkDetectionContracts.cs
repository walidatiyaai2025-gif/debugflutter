using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Application.Environment;

public enum FlutterSdkDetectionStatus
{
    Succeeded = 0,
    NotFound,
    ProbeFailed,
    TimedOut,
    Cancelled,
    ParseFailed
}

public sealed record FlutterSdkInstallation(
    string ExecutablePath,
    string SdkPath,
    string? Version,
    string? Channel,
    int PathIndex,
    bool IsPreferred,
    bool IsShadowed,
    string? ProbeMessage = null);

public sealed record FlutterSdkDetectionResult(
    FlutterSdkDetectionStatus Status,
    FlutterSdkInstallation? PreferredInstallation,
    IReadOnlyList<FlutterSdkInstallation> Installations,
    PathExecutableDiscoveryResult PathDiscovery,
    string? Message = null,
    ProcessResult? PreferredProbeResult = null)
{
    public bool IsSuccess => Status == FlutterSdkDetectionStatus.Succeeded;

    public bool HasConflict => PathDiscovery.HasConflict;
}

public interface IFlutterSdkDetector
{
    Task<FlutterSdkDetectionResult> DetectAsync(CancellationToken cancellationToken = default);
}
