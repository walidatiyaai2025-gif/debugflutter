using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Git.Cloning;

namespace FlutterBuildDoctor.Git.Repository;

public sealed class GitRepositoryRefreshService : IGitRepositoryRefreshService
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(15);

    private readonly IGitCloneService _cloneService;
    private readonly IGitWorkingTreeScanner _workingTreeScanner;
    private readonly IGitRepositoryIdentityService _identityService;
    private readonly IGitRefreshFileSystem _fileSystem;

    public GitRepositoryRefreshService(
        IGitCloneService cloneService,
        IGitWorkingTreeScanner workingTreeScanner,
        IGitRepositoryIdentityService identityService,
        IGitRefreshFileSystem? fileSystem = null)
    {
        _cloneService = cloneService ?? throw new ArgumentNullException(nameof(cloneService));
        _workingTreeScanner = workingTreeScanner ?? throw new ArgumentNullException(nameof(workingTreeScanner));
        _identityService = identityService ?? throw new ArgumentNullException(nameof(identityService));
        _fileSystem = fileSystem ?? new GitRefreshFileSystem();
    }

    public async Task<GitRepositoryRefreshResult> RefreshAsync(
        GitRepositoryRefreshRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (cancellationToken.IsCancellationRequested)
        {
            return Failure(
                GitRepositoryRefreshStatus.Cancelled,
                null,
                "Repository refresh was cancelled before it started.");
        }

        if (string.IsNullOrWhiteSpace(request.GitExecutablePath) ||
            string.IsNullOrWhiteSpace(request.RepositoryUrl) ||
            string.IsNullOrWhiteSpace(request.RepositoryPath))
        {
            return Failure(
                GitRepositoryRefreshStatus.InvalidRequest,
                null,
                "Git executable path, repository URL, and repository path are required.");
        }

        var timeout = request.Timeout ?? DefaultTimeout;
        if (timeout <= TimeSpan.Zero)
        {
            return Failure(
                GitRepositoryRefreshStatus.InvalidRequest,
                null,
                "Refresh timeout must be greater than zero.");
        }

        string repositoryPath;
        try
        {
            repositoryPath = _fileSystem.GetFullPath(request.RepositoryPath.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failure(
                GitRepositoryRefreshStatus.InvalidRequest,
                null,
                $"Repository path is invalid: {ex.Message}");
        }

        if (!_fileSystem.DirectoryExists(repositoryPath))
        {
            return Failure(
                GitRepositoryRefreshStatus.InvalidRepository,
                repositoryPath,
                "Repository directory does not exist.");
        }

        var gitMetadataPath = Path.Combine(repositoryPath, ".git");
        if (!_fileSystem.DirectoryExists(gitMetadataPath) && !_fileSystem.FileExists(gitMetadataPath))
        {
            return Failure(
                GitRepositoryRefreshStatus.InvalidRepository,
                repositoryPath,
                "Existing directory does not contain Git metadata.");
        }

        var workingTree = await _workingTreeScanner.ScanAsync(
            new GitWorkingTreeScanRequest(request.GitExecutablePath.Trim(), repositoryPath, timeout),
            progress,
            cancellationToken).ConfigureAwait(false);

        if (!workingTree.IsSuccess)
        {
            return new GitRepositoryRefreshResult(
                MapPreflightStatus(workingTree.Status),
                repositoryPath,
                OriginalWasDirty: false,
                Message: workingTree.Message ?? "Working-tree preflight failed.",
                WorkingTreeResult: workingTree);
        }

        var originalIdentity = await _identityService.ReadAsync(
            new GitRepositoryIdentityRequest(request.GitExecutablePath.Trim(), repositoryPath, timeout),
            progress,
            cancellationToken).ConfigureAwait(false);

        if (!originalIdentity.IsSuccess)
        {
            return new GitRepositoryRefreshResult(
                MapPreflightStatus(originalIdentity.Status),
                repositoryPath,
                OriginalWasDirty: workingTree.IsDirty,
                Message: originalIdentity.Message ?? "Repository identity preflight failed.",
                WorkingTreeResult: workingTree,
                IdentityResult: originalIdentity);
        }

        var parentPath = Path.GetDirectoryName(repositoryPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        var targetName = Path.GetFileName(repositoryPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));

        if (string.IsNullOrWhiteSpace(parentPath) || string.IsNullOrWhiteSpace(targetName))
        {
            return new GitRepositoryRefreshResult(
                GitRepositoryRefreshStatus.InvalidRequest,
                repositoryPath,
                OriginalWasDirty: workingTree.IsDirty,
                Message: "Repository path must resolve to a child directory that can be safely replaced.",
                WorkingTreeResult: workingTree,
                IdentityResult: originalIdentity);
        }

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var stagingName = $"{targetName}.fbd-staging-{suffix}";
        var stagingPath = Path.Combine(parentPath, stagingName);
        var backupPath = Path.Combine(parentPath, $"{targetName}.fbd-backup-{DateTime.UtcNow:yyyyMMddHHmmss}-{suffix}");
        var failedReplacementPath = Path.Combine(parentPath, $"{targetName}.fbd-failed-{suffix}");

        if (_fileSystem.DirectoryExists(stagingPath) ||
            _fileSystem.FileExists(stagingPath) ||
            _fileSystem.DirectoryExists(backupPath) ||
            _fileSystem.FileExists(backupPath) ||
            _fileSystem.DirectoryExists(failedReplacementPath) ||
            _fileSystem.FileExists(failedReplacementPath))
        {
            return new GitRepositoryRefreshResult(
                GitRepositoryRefreshStatus.InvalidRequest,
                repositoryPath,
                BackupPath: backupPath,
                StagingPath: stagingPath,
                OriginalWasDirty: workingTree.IsDirty,
                Message: "Generated refresh paths unexpectedly already exist; refresh was not started.",
                WorkingTreeResult: workingTree,
                IdentityResult: originalIdentity);
        }

        var cloneResult = await _cloneService.CloneAsync(
            new GitCloneRequest(
                request.GitExecutablePath.Trim(),
                request.RepositoryUrl.Trim(),
                parentPath,
                stagingName,
                timeout),
            progress,
            cancellationToken).ConfigureAwait(false);

        if (!cloneResult.IsSuccess)
        {
            TryDeleteGeneratedDirectory(stagingPath);
            return new GitRepositoryRefreshResult(
                MapCloneStatus(cloneResult.Status),
                repositoryPath,
                BackupPath: null,
                StagingPath: stagingPath,
                OriginalWasDirty: workingTree.IsDirty,
                Message: cloneResult.Message ?? "Staging clone failed; the original repository was not modified.",
                WorkingTreeResult: workingTree,
                CloneResult: cloneResult,
                IdentityResult: originalIdentity);
        }

        var stagingIdentity = await _identityService.ReadAsync(
            new GitRepositoryIdentityRequest(request.GitExecutablePath.Trim(), stagingPath, timeout),
            progress,
            cancellationToken).ConfigureAwait(false);

        if (!stagingIdentity.IsSuccess)
        {
            TryDeleteGeneratedDirectory(stagingPath);
            return new GitRepositoryRefreshResult(
                MapVerificationStatus(stagingIdentity.Status),
                repositoryPath,
                StagingPath: stagingPath,
                OriginalWasDirty: workingTree.IsDirty,
                Message: stagingIdentity.Message ?? "Staging clone could not be verified; the original repository was not modified.",
                WorkingTreeResult: workingTree,
                CloneResult: cloneResult,
                IdentityResult: stagingIdentity);
        }

        try
        {
            _fileSystem.MoveDirectory(repositoryPath, backupPath);
        }
        catch (Exception ex) when (IsFileSystemFailure(ex))
        {
            TryDeleteGeneratedDirectory(stagingPath);
            return new GitRepositoryRefreshResult(
                GitRepositoryRefreshStatus.BackupFailed,
                repositoryPath,
                BackupPath: backupPath,
                StagingPath: stagingPath,
                OriginalWasDirty: workingTree.IsDirty,
                Message: $"Existing repository could not be moved to backup: {ex.Message}",
                WorkingTreeResult: workingTree,
                CloneResult: cloneResult,
                IdentityResult: stagingIdentity);
        }

        try
        {
            _fileSystem.MoveDirectory(stagingPath, repositoryPath);
        }
        catch (Exception ex) when (IsFileSystemFailure(ex))
        {
            var rollback = TryRestoreBackup(backupPath, repositoryPath);
            return new GitRepositoryRefreshResult(
                rollback ? GitRepositoryRefreshStatus.SwapFailed : GitRepositoryRefreshStatus.RollbackFailed,
                repositoryPath,
                BackupPath: backupPath,
                StagingPath: stagingPath,
                OriginalWasDirty: workingTree.IsDirty,
                RollbackPerformed: rollback,
                Message: rollback
                    ? $"Replacement swap failed and the original repository was restored: {ex.Message}"
                    : $"Replacement swap failed and automatic rollback also failed. Backup remains at '{backupPath}'. Error: {ex.Message}",
                WorkingTreeResult: workingTree,
                CloneResult: cloneResult,
                IdentityResult: stagingIdentity);
        }

        var finalIdentity = await _identityService.ReadAsync(
            new GitRepositoryIdentityRequest(request.GitExecutablePath.Trim(), repositoryPath, timeout),
            progress,
            cancellationToken).ConfigureAwait(false);

        if (finalIdentity.IsSuccess)
        {
            return new GitRepositoryRefreshResult(
                GitRepositoryRefreshStatus.Succeeded,
                repositoryPath,
                BackupPath: backupPath,
                OriginalWasDirty: workingTree.IsDirty,
                Message: workingTree.IsDirty
                    ? "Repository refreshed successfully. The original dirty repository was preserved in the backup."
                    : "Repository refreshed successfully. The original repository was preserved in the backup.",
                WorkingTreeResult: workingTree,
                CloneResult: cloneResult,
                IdentityResult: finalIdentity);
        }

        var rollbackStatus = RollBackVerifiedReplacement(repositoryPath, backupPath, failedReplacementPath);
        return new GitRepositoryRefreshResult(
            rollbackStatus
                ? MapVerificationStatus(finalIdentity.Status)
                : GitRepositoryRefreshStatus.RollbackFailed,
            repositoryPath,
            BackupPath: backupPath,
            FailedReplacementPath: _fileSystem.DirectoryExists(failedReplacementPath) ? failedReplacementPath : null,
            OriginalWasDirty: workingTree.IsDirty,
            RollbackPerformed: rollbackStatus,
            Message: rollbackStatus
                ? $"Replacement verification failed and the original repository was restored. {finalIdentity.Message}"
                : $"Replacement verification failed and automatic rollback could not complete. Backup remains at '{backupPath}'. {finalIdentity.Message}",
            WorkingTreeResult: workingTree,
            CloneResult: cloneResult,
            IdentityResult: finalIdentity);
    }

    private static GitRepositoryRefreshResult Failure(
        GitRepositoryRefreshStatus status,
        string? repositoryPath,
        string message)
        => new(status, repositoryPath, Message: message);

    private static GitRepositoryRefreshStatus MapPreflightStatus(GitWorkingTreeScanStatus status)
        => status switch
        {
            GitWorkingTreeScanStatus.Cancelled => GitRepositoryRefreshStatus.Cancelled,
            GitWorkingTreeScanStatus.TimedOut => GitRepositoryRefreshStatus.TimedOut,
            _ => GitRepositoryRefreshStatus.PreflightFailed
        };

    private static GitRepositoryRefreshStatus MapPreflightStatus(GitRepositoryIdentityStatus status)
        => status switch
        {
            GitRepositoryIdentityStatus.Cancelled => GitRepositoryRefreshStatus.Cancelled,
            GitRepositoryIdentityStatus.TimedOut => GitRepositoryRefreshStatus.TimedOut,
            _ => GitRepositoryRefreshStatus.PreflightFailed
        };

    private static GitRepositoryRefreshStatus MapCloneStatus(GitCloneStatus status)
        => status switch
        {
            GitCloneStatus.Cancelled => GitRepositoryRefreshStatus.Cancelled,
            GitCloneStatus.TimedOut => GitRepositoryRefreshStatus.TimedOut,
            _ => GitRepositoryRefreshStatus.CloneFailed
        };

    private static GitRepositoryRefreshStatus MapVerificationStatus(GitRepositoryIdentityStatus status)
        => status switch
        {
            GitRepositoryIdentityStatus.Cancelled => GitRepositoryRefreshStatus.Cancelled,
            GitRepositoryIdentityStatus.TimedOut => GitRepositoryRefreshStatus.TimedOut,
            _ => GitRepositoryRefreshStatus.VerificationFailed
        };

    private static bool IsFileSystemFailure(Exception ex)
        => ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException;

    private bool TryRestoreBackup(string backupPath, string repositoryPath)
    {
        try
        {
            if (_fileSystem.DirectoryExists(repositoryPath) || _fileSystem.FileExists(repositoryPath))
            {
                return false;
            }

            _fileSystem.MoveDirectory(backupPath, repositoryPath);
            return true;
        }
        catch (Exception ex) when (IsFileSystemFailure(ex))
        {
            return false;
        }
    }

    private bool RollBackVerifiedReplacement(
        string repositoryPath,
        string backupPath,
        string failedReplacementPath)
    {
        try
        {
            _fileSystem.MoveDirectory(repositoryPath, failedReplacementPath);
            _fileSystem.MoveDirectory(backupPath, repositoryPath);
            return true;
        }
        catch (Exception ex) when (IsFileSystemFailure(ex))
        {
            return false;
        }
    }

    private void TryDeleteGeneratedDirectory(string path)
    {
        try
        {
            if (_fileSystem.DirectoryExists(path))
            {
                _fileSystem.DeleteDirectory(path, recursive: true);
            }
        }
        catch (Exception ex) when (IsFileSystemFailure(ex))
        {
            // Best-effort cleanup of a directory created only for this refresh attempt.
        }
    }
}
