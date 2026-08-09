namespace FlutterBuildDoctor.Flutter.ProjectAnalysis;

public enum GradleWrapperParseStatus
{
    Succeeded = 0,
    ProjectRootUnavailable,
    WrapperDirectoryMissing,
    PropertiesFileMissing,
    UnsafePath,
    FileTooLarge,
    ReadFailed,
    InvalidProperties,
    DistributionUrlMissing,
    VersionNotDetected
}

public enum GradleDistributionType
{
    Unknown = 0,
    Bin,
    All
}

public sealed record GradleWrapperParseResult(
    GradleWrapperParseStatus Status,
    FlutterProjectRootResult ProjectRoot,
    string? WrapperPropertiesPath,
    string? DistributionUrl,
    string? GradleVersion,
    GradleDistributionType DistributionType,
    string? RawText,
    string Message)
{
    public bool IsSuccess => Status == GradleWrapperParseStatus.Succeeded;
}

public interface IGradleWrapperParser
{
    GradleWrapperParseResult Parse(FlutterProjectRootResult projectRoot);
}
