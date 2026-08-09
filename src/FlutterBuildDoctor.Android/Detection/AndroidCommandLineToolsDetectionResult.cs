namespace FlutterBuildDoctor.Android.Detection;

public enum AndroidCommandLineToolsDetectionStatus
{
    Succeeded = 0,
    AndroidSdkRootUnavailable,
    CommandLineToolsMissing,
    EffectiveSdkManagerMissing,
    MetadataInvalid
}

public enum AndroidCommandLineToolsLayout
{
    Versioned = 0,
    LatestAlias,
    LegacyTools
}

public sealed record AndroidCommandLineToolsCandidate(
    string InstallationPath,
    string? SdkManagerPath,
    string? Revision,
    AndroidCommandLineToolsLayout Layout,
    bool IsEffective,
    bool SdkManagerExists,
    string? SourcePropertiesPath,
    string? RawSourceProperties,
    string? Message)
{
    public bool IsUsable => SdkManagerExists && !string.IsNullOrWhiteSpace(Revision);
}

public sealed record AndroidCommandLineToolsDetectionResult(
    AndroidCommandLineToolsDetectionStatus Status,
    string AndroidSdkRoot,
    AndroidCommandLineToolsCandidate? EffectiveCandidate,
    IReadOnlyList<AndroidCommandLineToolsCandidate> Candidates,
    bool HasMultipleInstallations,
    string Message)
{
    public bool IsSuccess => Status == AndroidCommandLineToolsDetectionStatus.Succeeded;
}

public interface IAndroidCommandLineToolsDetector
{
    AndroidCommandLineToolsDetectionResult Detect(AndroidSdkRootDetectionResult sdkRootResult);
}
