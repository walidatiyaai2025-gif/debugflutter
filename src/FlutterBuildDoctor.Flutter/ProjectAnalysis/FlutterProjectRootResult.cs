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
    bool HasMetadataFile,
    bool HasLibDirectory,
    bool HasAndroidDirectory,
    bool HasIosDirectory,
    bool HasWebDirectory,
    bool HasWindowsDirectory,
    bool HasMacOsDirectory,
    bool HasLinuxDirectory)
{
    public bool HasPlatformDirectory =>
        HasAndroidDirectory ||
        HasIosDirectory ||
        HasWebDirectory ||
        HasWindowsDirectory ||
        HasMacOsDirectory ||
        HasLinuxDirectory;

    public bool HasFlutterProjectEvidence =>
        HasMetadataFile || (HasLibDirectory && HasPlatformDirectory);
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
