using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Git.Branches;

public enum GitPullStatus
{
    UpToDate = 0,
    FastForwarded,
    InvalidRequest,
    InvalidRepository,
    DetachedHead,
    NoUpstream,
    Failed,
    Cancelled,
    TimedOut,
    VerificationFailed
}

public sealed record GitPullRequest(
    string GitExecutablePath,
    string RepositoryPath,
    TimeSpan? Timeout = null);

public sealed record GitPullResult(
    GitPullStatus Status,
    string? CurrentBranch = null,
    string? Upstream = null,
    string? BeforeCommitSha = null,
    string? AfterCommitSha = null,
    string? Message = null,
    ProcessResult? BranchProbeResult = null,
    ProcessResult? UpstreamProbeResult = null,
    ProcessResult? BeforeHeadResult = null,
    ProcessResult? PullProcessResult = null,
    ProcessResult? PostBranchProbeResult = null,
    ProcessResult? AfterHeadResult = null)
{
    public bool IsSuccess => Status is GitPullStatus.UpToDate or GitPullStatus.FastForwarded;

    public bool Changed => Status == GitPullStatus.FastForwarded;
}

public interface IGitPullService
{
    Task<GitPullResult> PullAsync(
        GitPullRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default);
}
