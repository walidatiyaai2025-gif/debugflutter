using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Git.Branches;

public enum GitBranchSwitchStatus
{
    Succeeded = 0,
    InvalidRequest,
    InvalidRepository,
    InvalidBranch,
    Failed,
    Cancelled,
    TimedOut,
    VerificationFailed
}

public sealed record GitBranchSwitchRequest(
    string GitExecutablePath,
    string RepositoryPath,
    GitBranchInfo Branch,
    TimeSpan? Timeout = null);

public sealed record GitBranchSwitchResult(
    GitBranchSwitchStatus Status,
    string? CurrentBranch = null,
    string? CommitSha = null,
    string? Message = null,
    ProcessResult? SwitchResult = null,
    ProcessResult? BranchVerificationResult = null,
    ProcessResult? CommitVerificationResult = null)
{
    public bool IsSuccess => Status == GitBranchSwitchStatus.Succeeded;
}

public interface IGitBranchSwitcher
{
    Task<GitBranchSwitchResult> SwitchAsync(
        GitBranchSwitchRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default);
}
