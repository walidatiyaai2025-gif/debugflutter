namespace FlutterBuildDoctor.Application.Environment;

public enum AndroidStudioDetectionStatus
{
    Succeeded = 0,
    NotWindows,
    Missing,
    InspectionFailed
}

public enum AndroidStudioDiscoverySource
{
    ProgramFiles = 0,
    ProgramFilesX86,
    LocalAppDataPrograms,
    JetBrainsToolbox
}

public enum AndroidStudioMetadataSource
{
    None = 0,
    ProductInfoJson,
    BuildTxt,
    ExecutableFileVersion
}

public sealed record AndroidStudioExecutableEvidence(
    string ExecutablePath,
    AndroidStudioDiscoverySource DiscoverySource);

public sealed record AndroidStudioInstallation(
    string ExecutablePath,
    string InstallationPath,
    string? ProductName,
    string? Version,
    string? BuildNumber,
    string? ProductCode,
    AndroidStudioDiscoverySource DiscoverySource,
    AndroidStudioMetadataSource MetadataSource,
    string? RawMetadata,
    string? Message);

public sealed record AndroidStudioDetectionResult(
    AndroidStudioDetectionStatus Status,
    IReadOnlyList<AndroidStudioInstallation> Installations,
    string Message)
{
    public bool IsSuccess => Status == AndroidStudioDetectionStatus.Succeeded;
}

public interface IAndroidStudioDetector
{
    AndroidStudioDetectionResult Detect(WindowsEnvironmentInfo windowsEnvironment);
}
