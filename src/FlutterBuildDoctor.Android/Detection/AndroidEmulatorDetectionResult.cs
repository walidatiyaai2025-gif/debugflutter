using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Android.Detection;

public enum AndroidEmulatorDetectionStatus
{
    Succeeded = 0,
    AndroidSdkRootUnavailable,
    EmulatorDirectoryMissing,
    EmulatorMissing,
    ProbeFailed,
    TimedOut,
    Cancelled,
    VersionUnavailable
}

public enum AndroidEmulatorVersionSource
{
    None = 0,
    CommandOutput,
    SourceProperties
}

public sealed record AndroidEmulatorDetectionResult(
    AndroidEmulatorDetectionStatus Status,
    string AndroidSdkRoot,
    string? EmulatorDirectory,
    string? EmulatorPath,
    string? Version,
    AndroidEmulatorVersionSource VersionSource,
    string? RawVersionOutput,
    string? RawSourceProperties,
    string Message,
    ProcessResult? ProbeResult = null)
{
    public bool IsSuccess => Status == AndroidEmulatorDetectionStatus.Succeeded;
}

public interface IAndroidEmulatorDetector
{
    Task<AndroidEmulatorDetectionResult> DetectAsync(
        AndroidSdkRootDetectionResult sdkRootResult,
        CancellationToken cancellationToken = default);
}
