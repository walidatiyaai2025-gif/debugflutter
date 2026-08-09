namespace FlutterBuildDoctor.Android.Detection;

public enum AndroidBuildToolsDetectionStatus
{
    Succeeded = 0,
    AndroidSdkRootUnavailable,
    BuildToolsDirectoryMissing,
    NoBuildToolsInstalled,
    PartialInstallationsOnly,
    InspectionFailed
}

public sealed record AndroidBuildToolsPackage(
    string DirectoryName,
    string InstallationPath,
    string? Revision,
    bool Aapt2Exists,
    bool ZipAlignExists,
    bool D8Exists,
    bool ApkSignerExists,
    string? SourcePropertiesPath,
    string? RawSourceProperties,
    string? Message)
{
    public bool IsUsable => !string.IsNullOrWhiteSpace(Revision) &&
                            Aapt2Exists &&
                            ZipAlignExists &&
                            D8Exists &&
                            ApkSignerExists;
}

public sealed record AndroidBuildToolsDetectionResult(
    AndroidBuildToolsDetectionStatus Status,
    string AndroidSdkRoot,
    IReadOnlyList<AndroidBuildToolsPackage> Packages,
    string Message)
{
    public bool IsSuccess => Status == AndroidBuildToolsDetectionStatus.Succeeded;

    public IReadOnlyList<string> InstalledVersions => Packages
        .Where(package => package.IsUsable && !string.IsNullOrWhiteSpace(package.Revision))
        .Select(package => package.Revision!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(version => ParseVersion(version))
        .ThenByDescending(version => version, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static Version ParseVersion(string value)
        => Version.TryParse(value, out var parsed) ? parsed : new Version(0, 0);
}

public interface IAndroidBuildToolsDetector
{
    AndroidBuildToolsDetectionResult Detect(AndroidSdkRootDetectionResult sdkRootResult);
}
