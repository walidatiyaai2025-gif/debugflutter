namespace FlutterBuildDoctor.Android.Detection;

public enum AndroidPlatformDetectionStatus
{
    Succeeded = 0,
    AndroidSdkRootUnavailable,
    PlatformsDirectoryMissing,
    NoPlatformsInstalled,
    PartialInstallationsOnly,
    InspectionFailed
}

public sealed record AndroidPlatformPackage(
    string PackageId,
    string InstallationPath,
    int? ApiLevel,
    string? CodeName,
    string? Revision,
    bool AndroidJarExists,
    bool FrameworkAidlExists,
    string? SourcePropertiesPath,
    string? RawSourceProperties,
    string? Message)
{
    public bool IsPreview => !string.IsNullOrWhiteSpace(CodeName) &&
                             !string.Equals(CodeName, "REL", StringComparison.OrdinalIgnoreCase);

    public bool IsUsable => ApiLevel is not null && AndroidJarExists;
}

public sealed record AndroidPlatformDetectionResult(
    AndroidPlatformDetectionStatus Status,
    string AndroidSdkRoot,
    IReadOnlyList<AndroidPlatformPackage> Platforms,
    string Message)
{
    public bool IsSuccess => Status == AndroidPlatformDetectionStatus.Succeeded;

    public IReadOnlyList<int> InstalledApiLevels => Platforms
        .Where(platform => platform.IsUsable && platform.ApiLevel is not null)
        .Select(platform => platform.ApiLevel!.Value)
        .Distinct()
        .OrderByDescending(level => level)
        .ToArray();
}

public interface IAndroidPlatformDetector
{
    AndroidPlatformDetectionResult Detect(AndroidSdkRootDetectionResult sdkRootResult);
}
