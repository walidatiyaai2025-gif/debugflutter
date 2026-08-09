using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Git.Repository;

public enum GitWorkingTreeScanStatus
{
    Succeeded = 0,
    InvalidRequest,
    InvalidRepository,
    Failed,
    Cancelled,
    TimedOut,
    ParseFailed
}

public enum GitWorkingTreeChangeKind
{
    Modified = 0,
    Added,
    Deleted,
    Renamed,
    Copied,
    TypeChanged,
    Unmerged,
    Untracked,
    Unknown
}

public sealed record GitWorkingTreeChange(
    string Path,
    GitWorkingTreeChangeKind Kind,
    string StatusCode,
    bool IsStaged,
    bool IsUnstaged,
    string? OriginalPath = null);

public sealed record GitWorkingTreeScanRequest(
    string GitExecutablePath,
    string RepositoryPath,
    TimeSpan? Timeout = null);

public sealed record GitWorkingTreeScanResult(
    GitWorkingTreeScanStatus Status,
    IReadOnlyList<GitWorkingTreeChange> Changes,
    string? Message = null,
    string? RawStatus = null,
    ProcessResult? ProcessResult = null)
{
    public bool IsSuccess => Status == GitWorkingTreeScanStatus.Succeeded;

    public bool IsDirty => IsSuccess && Changes.Count > 0;

    public bool IsClean => IsSuccess && Changes.Count == 0;
}

public interface IGitWorkingTreeScanner
{
    Task<GitWorkingTreeScanResult> ScanAsync(
        GitWorkingTreeScanRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default);
}
