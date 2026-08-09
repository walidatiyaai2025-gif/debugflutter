namespace FlutterBuildDoctor.Git.Repository;

/// <summary>
/// Decorates the raw refresh file-system operations with bounded Windows lock recovery.
/// The original safe-refresh workflow remains authoritative: this component only retries
/// a directory move after terminating processes that Windows reports as owning handles
/// under the source workspace.
/// </summary>
public sealed class LockRecoveringGitRefreshFileSystem : IGitRefreshFileSystem
{
    private const int RetryCount = 5;
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromMilliseconds(150);

    private readonly IGitRefreshFileSystem _inner;
    private readonly IGitWorkspaceLockResolver _lockResolver;

    public LockRecoveringGitRefreshFileSystem(
        IGitRefreshFileSystem inner,
        IGitWorkspaceLockResolver lockResolver)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _lockResolver = lockResolver ?? throw new ArgumentNullException(nameof(lockResolver));
    }

    public bool DirectoryExists(string path)
        => _inner.DirectoryExists(path);

    public bool FileExists(string path)
        => _inner.FileExists(path);

    public string GetFullPath(string path)
        => _inner.GetFullPath(path);

    public void MoveDirectory(string sourceDirectoryName, string destinationDirectoryName)
    {
        try
        {
            _inner.MoveDirectory(sourceDirectoryName, destinationDirectoryName);
            return;
        }
        catch (Exception ex) when (IsLockCandidate(ex))
        {
            var recovery = ReleaseLocksSafely(sourceDirectoryName, ex);
            Exception lastError = ex;

            for (var attempt = 1; attempt <= RetryCount; attempt++)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(InitialRetryDelay.TotalMilliseconds * attempt));

                try
                {
                    _inner.MoveDirectory(sourceDirectoryName, destinationDirectoryName);
                    return;
                }
                catch (Exception retryError) when (IsLockCandidate(retryError))
                {
                    lastError = retryError;
                }
            }

            throw new IOException(
                $"Directory move still failed after Windows workspace lock recovery. " +
                $"{recovery.Message} Last error: {lastError.Message}",
                lastError);
        }
    }

    public void DeleteDirectory(string path, bool recursive)
        => _inner.DeleteDirectory(path, recursive);

    private WorkspaceLockReleaseResult ReleaseLocksSafely(string sourceDirectoryName, Exception originalError)
    {
        try
        {
            return _lockResolver.ReleaseLocks(sourceDirectoryName);
        }
        catch (Exception recoveryError)
        {
            return new WorkspaceLockReleaseResult(
                Supported: OperatingSystem.IsWindows(),
                RegisteredFileCount: 0,
                Processes: Array.Empty<WorkspaceLockProcessResult>(),
                Message:
                    $"Lock inspection failed: {recoveryError.Message}. " +
                    $"Original move error: {originalError.Message}");
        }
    }

    private static bool IsLockCandidate(Exception exception)
        => exception is IOException or UnauthorizedAccessException;
}
