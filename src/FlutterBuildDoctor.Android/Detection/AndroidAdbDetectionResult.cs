using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Android.Detection;

public enum AndroidAdbDetectionStatus
{
    Succeeded = 0,
    AndroidSdkRootUnavailable,
    PlatformToolsMissing,
    AdbMissing,
    ProbeFailed,
    TimedOut,
    Cancelled,
    ParseFailed
}

public sealed record AndroidAdbDetectionResult(
    AndroidAdbDetectionStatus Status,
    string AndroidSdkRoot,
    string? PlatformToolsPath,
    string? AdbPath,
    string? AdbProtocolVersion,
    string? PlatformToolsVersion,
    string? InstalledAsPath,
    string? RawVersionOutput,
    string? RawSourceProperties,
    string Message,
    ProcessResult? ProbeResult = null)
{
    public bool IsSuccess => Status == AndroidAdbDetectionStatus.Succeeded;
}

public interface IAndroidAdbDetector
{
    Task<AndroidAdbDetectionResult> DetectAsync(
        AndroidSdkRootDetectionResult sdkRootResult,
        CancellationToken cancellationToken = default);
}
