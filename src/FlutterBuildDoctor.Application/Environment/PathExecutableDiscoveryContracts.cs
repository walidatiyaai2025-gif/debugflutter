namespace FlutterBuildDoctor.Application.Environment;

public enum PathExecutableDiscoveryStatus
{
    Succeeded = 0,
    InvalidRequest
}

public sealed record PathExecutableDiscoveryRequest(
    string ExecutableName,
    string? PathValue = null,
    string? PathExtValue = null);

public sealed record PathExecutableMatch(
    string FullPath,
    string DirectoryPath,
    string ResolvedFileName,
    string Extension,
    int PathIndex,
    int ResolutionOrder,
    bool IsPreferred,
    bool IsShadowed);

public sealed record IgnoredPathEntry(
    int PathIndex,
    string RawValue,
    string Reason);

public sealed record PathExecutableDiscoveryResult(
    PathExecutableDiscoveryStatus Status,
    string ExecutableName,
    IReadOnlyList<PathExecutableMatch> Matches,
    IReadOnlyList<string> SearchDirectories,
    IReadOnlyList<string> Extensions,
    IReadOnlyList<IgnoredPathEntry> IgnoredPathEntries,
    string? Message = null)
{
    public bool IsSuccess => Status == PathExecutableDiscoveryStatus.Succeeded;

    public bool IsFound => IsSuccess && Matches.Count > 0;

    public bool HasConflict => IsSuccess && Matches.Count > 1;

    public PathExecutableMatch? PreferredMatch
        => Matches.FirstOrDefault(static match => match.IsPreferred);
}

public interface IPathExecutableDiscovery
{
    PathExecutableDiscoveryResult Discover(PathExecutableDiscoveryRequest request);
}
