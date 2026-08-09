namespace FlutterBuildDoctor.Flutter.ProjectAnalysis;

public enum GradleWrapperVersionStatus
{
    Succeeded = 0,
    ProjectRootUnavailable,
    AndroidDirectoryMissing,
    WrapperPropertiesMissing,
    UnsafePath,
    FileTooLarge,
    ReadFailed,
    InvalidProperties,
    DistributionUrlMissing,
    DistributionUrlInvalid,
    VersionNotFound,
    InspectionFailed
}

public enum GradleDistributionKind
{
    Unknown = 0,
    Bin,
    All
}

public sealed record GradleWrapperVersionResult(
    GradleWrapperVersionStatus Status,
    FlutterProjectRootResult ProjectRoot,
    string? PropertiesPath,
    string? DistributionUrl,
    string? Version,
    GradleDistributionKind DistributionKind,
    string Message)
{
    public bool IsSuccess => Status == GradleWrapperVersionStatus.Succeeded;
}

public interface IGradleWrapperVersionParser
{
    GradleWrapperVersionResult Parse(FlutterProjectRootResult projectRoot);
}