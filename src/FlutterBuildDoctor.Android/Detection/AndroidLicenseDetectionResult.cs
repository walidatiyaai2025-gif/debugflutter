using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Android.Detection;

public enum AndroidLicenseDetectionStatus
{
    Accepted = 0,
    Pending,
    SdkManagerUnavailable,
    ProbeFailed,
    TimedOut,
    Cancelled,
    Indeterminate
}

public sealed record AndroidLicenseDetectionResult(
    AndroidLicenseDetectionStatus Status,
    string AndroidSdkRoot,
    string? SdkManagerPath,
    string? CommandLineToolsRevision,
    IReadOnlyList<string> LicenseFiles,
    string? RawOutput,
    string Message,
    ProcessResult? ProbeResult = null)
{
    public bool IsReady => Status == AndroidLicenseDetectionStatus.Accepted;
}

public interface IAndroidLicenseDetector
{
    Task<AndroidLicenseDetectionResult> DetectAsync(
        AndroidCommandLineToolsDetectionResult commandLineToolsResult,
        CancellationToken cancellationToken = default);
}
