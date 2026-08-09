namespace FlutterBuildDoctor.Flutter.ProjectAnalysis;

public enum FlutterProjectRootStatus
{
    Succeeded = 0,
    InvalidRequest,
    RepositoryNotFound,
    PubspecNotFound,
    NotFlutterProject,
    Ambiguous,
    InspectionFailed
}

public sealed record FlutterProjectCandidate(
    string RootPath,
    string PubspecPath,
    bool HasFlutterSdkDependency,
    bool HasMetadataFile,
    bool HasLibDirectory,
    bool HasAndroidDirectory)
{
    public bool IsFlutterProject => HasFlutterSdkDependency || HasMetadataFile;
}

public sealed record FlutterProjectRootResult(
    FlutterProjectRootStatus Status,
    string? RepositoryPath,
    string? EffectiveRoot,
    string? EffectivePubspecPath,
    IReadOnlyList<FlutterProjectCandidate> Candidates,
    IReadOnlyList<string> InspectedPubspecPaths,
    string Message)
{
    public bool IsSuccess => Status == FlutterProjectRootStatus.Succeeded;
}

public interface IFlutterProjectRootLocator
{
    FlutterProjectRootResult Locate(string repositoryPath);
}
