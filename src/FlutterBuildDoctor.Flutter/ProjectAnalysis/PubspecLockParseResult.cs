namespace FlutterBuildDoctor.Flutter.ProjectAnalysis;

public enum PubspecLockParseStatus
{
    Succeeded = 0,
    InvalidRequest,
    ProjectRootUnavailable,
    LockFileNotFound,
    FileTooLarge,
    ReadFailed,
    MalformedYaml,
    InvalidDocument
}

public enum PubspecLockedPackageSource
{
    Hosted = 0,
    Git,
    Path,
    Sdk,
    Unknown
}

public sealed record PubspecLockedPackage(
    string Name,
    string Version,
    PubspecLockedPackageSource Source,
    string? DependencyType,
    string? DescriptionName,
    string? DescriptionUrl,
    string? DescriptionPath,
    string? GitResolvedRef,
    string? GitUrl);

public sealed record PubspecLockMetadata(
    IReadOnlyList<PubspecLockedPackage> Packages,
    string? DartSdkConstraint,
    string? FlutterSdkConstraint)
{
    public PubspecLockedPackage? FindPackage(string name)
        => Packages.FirstOrDefault(package =>
            string.Equals(package.Name, name, StringComparison.OrdinalIgnoreCase));
}

public sealed record PubspecLockParseResult(
    PubspecLockParseStatus Status,
    FlutterProjectRootResult ProjectRoot,
    string? LockFilePath,
    PubspecLockMetadata? Metadata,
    string? RawText,
    string Message)
{
    public bool IsSuccess => Status == PubspecLockParseStatus.Succeeded;
}

public interface IPubspecLockParser
{
    PubspecLockParseResult Parse(FlutterProjectRootResult projectRoot);
}
