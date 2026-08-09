namespace FlutterBuildDoctor.Flutter.ProjectAnalysis;

public enum PubspecParseStatus
{
    Succeeded = 0,
    InvalidRequest,
    ProjectRootUnavailable,
    PubspecNotFound,
    FileTooLarge,
    ReadFailed,
    MalformedYaml,
    InvalidDocument,
    MissingRequiredField
}

public enum PubspecDependencySection
{
    Dependencies = 0,
    DevDependencies,
    DependencyOverrides
}

public enum PubspecDependencyKind
{
    Hosted = 0,
    Sdk,
    Git,
    Path,
    Unknown
}

public sealed record PubspecDependency(
    string Name,
    PubspecDependencySection Section,
    PubspecDependencyKind Kind,
    string? Constraint,
    string? Sdk,
    string? Path,
    string? GitUrl,
    string? GitRef,
    string? GitPath,
    string? HostedUrl,
    string? HostedName);

public sealed record PubspecMetadata(
    string Name,
    string? Description,
    string? Version,
    string? PublishTo,
    string? Homepage,
    string? Repository,
    string? IssueTracker,
    string? Documentation,
    string? DartSdkConstraint,
    string? FlutterSdkConstraint,
    IReadOnlyList<string> Topics,
    IReadOnlyList<PubspecDependency> Dependencies)
{
    public bool HasFlutterSdkDependency
        => Dependencies.Any(dependency =>
            dependency.Kind == PubspecDependencyKind.Sdk &&
            string.Equals(dependency.Sdk, "flutter", StringComparison.OrdinalIgnoreCase));
}

public sealed record PubspecParseResult(
    PubspecParseStatus Status,
    FlutterProjectRootResult ProjectRoot,
    string? PubspecPath,
    PubspecMetadata? Metadata,
    string? RawText,
    string Message)
{
    public bool IsSuccess => Status == PubspecParseStatus.Succeeded;
}

public interface IPubspecParser
{
    PubspecParseResult Parse(FlutterProjectRootResult projectRoot);
}
