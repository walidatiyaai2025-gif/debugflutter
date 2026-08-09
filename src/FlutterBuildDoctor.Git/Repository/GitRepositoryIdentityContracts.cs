using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Git.Repository;

public enum GitRepositoryIdentityStatus
{
    Succeeded = 0,
    InvalidRequest,
    InvalidRepository,
    Failed,
    Cancelled,
    TimedOut,
    VerificationFailed
}

public sealed record GitRepositoryIdentity(
    string RepositoryPath,
    string CommitSha,
    string? BranchName = null,
    string? Upstream = null,
    string? RemoteName = null,
    bool IsDetached = false)
{
    public string ShortCommitSha
        => CommitSha.Length <= 12 ? CommitSha : CommitSha[..12];
}

public sealed record GitRepositoryIdentityRequest(
    string GitExecutablePath,
    string RepositoryPath,
    TimeSpan? Timeout = null);

public sealed record GitRepositoryIdentityResult(
    GitRepositoryIdentityStatus Status,
    GitRepositoryIdentity? Identity = null,
    string? Message = null,
    ProcessResult? BranchResult = null,
    ProcessResult? CommitResult = null,
    ProcessResult? UpstreamResult = null,
    ProcessResult? RemoteResult = null)
{
    public bool IsSuccess => Status == GitRepositoryIdentityStatus.Succeeded;
}

public interface IGitRepositoryIdentityService
{
    Task<GitRepositoryIdentityResult> ReadAsync(
        GitRepositoryIdentityRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default);
}
