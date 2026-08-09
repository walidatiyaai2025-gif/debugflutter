namespace FlutterBuildDoctor.Android.Detection;

public enum AndroidAvdManagerDetectionStatus
{
    Succeeded = 0,
    CommandLineToolsUnavailable,
    AvdManagerMissing
}

public sealed record AndroidAvdManagerCandidate(
    string InstallationPath,
    string? AvdManagerPath,
    string? CommandLineToolsRevision,
    AndroidCommandLineToolsLayout Layout,
    bool IsEffective,
    bool Exists,
    string? Message);

public sealed record AndroidAvdManagerDetectionResult(
    AndroidAvdManagerDetectionStatus Status,
    string AndroidSdkRoot,
    AndroidAvdManagerCandidate? EffectiveCandidate,
    IReadOnlyList<AndroidAvdManagerCandidate> Candidates,
    bool HasMultipleInstallations,
    string Message)
{
    public bool IsSuccess => Status == AndroidAvdManagerDetectionStatus.Succeeded;
}

public interface IAndroidAvdManagerDetector
{
    AndroidAvdManagerDetectionResult Detect(AndroidCommandLineToolsDetectionResult commandLineToolsResult);
}
