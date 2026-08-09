using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Git.Cloning;

namespace FlutterBuildDoctor.Git.Repository;

public enum GitRepositoryRefreshStatus
{
    Succeeded = 0,
    InvalidRequest,
    InvalidRepository,
    PreflightFailed,
    CloneFailed,
    BackupFailed,
    SwapFailed,
    VerificationFailed,
    RollbackFailed,
    Cancelled,
    TimedOut
}

public sealed record GitRepositoryRefreshRequest(
    string GitExecutablePath,
    string RepositoryUrl,
    string RepositoryPath,
    TimeSpan? Timeout = null);

public sealed record GitRepositoryRefreshResult(
    GitRepositoryRefreshStatus Status,
    string? RepositoryPath,
    string? BackupPath = null,
    string? StagingPath = null,
    string? FailedReplacementPath = null,
    bool OriginalWasDirty = false,
    bool RollbackPerformed = false,
    string? Message = null,
    GitWorkingTreeScanResult? WorkingTreeResult = null,
    GitCloneResult? CloneResult = null,
    GitRepositoryIdentityResult? IdentityResult = null)
{
    public bool IsSuccess => Status == GitRepositoryRefreshStatus.Succeeded;
}

public interface IGitRepositoryRefreshService
{
    Task<GitRepositoryRefreshResult> RefreshAsync(
        GitRepositoryRefreshRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default);
}
