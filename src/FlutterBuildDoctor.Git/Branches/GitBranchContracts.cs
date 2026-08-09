using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Git.Branches;

public enum GitBranchKind
{
    Local = 0,
    Remote
}

public enum GitBranchDiscoveryStatus
{
    Succeeded = 0,
    SucceededWithWarning,
    InvalidRequest,
    InvalidRepository,
    Failed,
    Cancelled,
    TimedOut
}

public sealed record GitBranchInfo(
    string Name,
    string FullName,
    GitBranchKind Kind,
    string CommitSha,
    bool IsCurrent = false,
    string? RemoteName = null,
    string? Upstream = null);

public sealed record GitBranchDiscoveryRequest(
    string GitExecutablePath,
    string RepositoryPath,
    bool RefreshRemotes = true,
    TimeSpan? Timeout = null);

public sealed record GitBranchDiscoveryResult(
    GitBranchDiscoveryStatus Status,
    IReadOnlyList<GitBranchInfo> Branches,
    string? Message = null,
    ProcessResult? RefreshResult = null,
    ProcessResult? ListResult = null)
{
    public bool IsSuccess => Status is
        GitBranchDiscoveryStatus.Succeeded or
        GitBranchDiscoveryStatus.SucceededWithWarning;
}

public interface IGitBranchService
{
    Task<GitBranchDiscoveryResult> GetBranchesAsync(
        GitBranchDiscoveryRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default);
}
