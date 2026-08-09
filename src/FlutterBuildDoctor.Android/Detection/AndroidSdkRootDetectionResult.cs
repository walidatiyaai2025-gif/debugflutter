using FlutterBuildDoctor.Application.Environment;

namespace FlutterBuildDoctor.Android.Detection;

public enum AndroidSdkRootDetectionStatus
{
    Succeeded = 0,
    MissingEffectiveRoot,
    EffectiveRootInvalid
}

public sealed record AndroidSdkRootSourceEvidence(
    string VariableName,
    VariableScope Scope,
    string RawValue);

public sealed record AndroidSdkRootCandidate(
    string NormalizedPath,
    IReadOnlyList<AndroidSdkRootSourceEvidence> Sources,
    bool IsEffective,
    bool Exists,
    bool HasRecognizedSdkLayout,
    bool HasPlatformToolsDirectory,
    bool HasPlatformsDirectory,
    bool HasBuildToolsDirectory,
    bool HasCmdlineToolsDirectory,
    bool HasLicensesDirectory,
    string? ValidationMessage)
{
    public bool IsValid => Exists && HasRecognizedSdkLayout;
}

public sealed record AndroidSdkRootDetectionResult(
    AndroidSdkRootDetectionStatus Status,
    AndroidSdkRootCandidate? EffectiveCandidate,
    IReadOnlyList<AndroidSdkRootCandidate> Candidates,
    bool HasConflict,
    string Message)
{
    public bool IsSuccess => Status == AndroidSdkRootDetectionStatus.Succeeded;
}

public interface IAndroidSdkRootDetector
{
    AndroidSdkRootDetectionResult Detect(EnvironmentVariableSnapshot snapshot);
}
