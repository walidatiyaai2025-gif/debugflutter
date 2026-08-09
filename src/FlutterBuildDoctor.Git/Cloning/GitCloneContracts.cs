using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Git.Cloning;

public enum GitCloneStatus
{
    Succeeded = 0,
    InvalidRequest,
    InvalidRepositoryUrl,
    InvalidWorkspace,
    InvalidTargetDirectory,
    TargetNotEmpty,
    Failed,
    Cancelled,
    TimedOut
}

public sealed record GitCloneRequest(
    string GitExecutablePath,
    string RepositoryUrl,
    string WorkspaceDirectory,
    string? TargetDirectoryName = null,
    TimeSpan? Timeout = null);

public sealed record GitCloneResult(
    GitCloneStatus Status,
    string? RepositoryPath,
    string? Message,
    ProcessResult? ProcessResult = null)
{
    public bool IsSuccess => Status == GitCloneStatus.Succeeded;
}

public interface IGitCloneService
{
    Task<GitCloneResult> CloneAsync(
        GitCloneRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default);
}
