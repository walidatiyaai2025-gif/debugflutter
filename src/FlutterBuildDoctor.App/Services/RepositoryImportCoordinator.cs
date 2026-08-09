using System.IO;
using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Git.Branches;
using FlutterBuildDoctor.Git.Cloning;
using FlutterBuildDoctor.Git.Repository;

namespace FlutterBuildDoctor.App.Services;

public enum RepositoryImportStatus
{
    Succeeded = 0,
    InvalidRequest,
    ExistingTargetIsNotGit,
    CloneFailed,
    RefreshFailed,
    BranchDiscoveryFailed,
    BranchNotFound,
    BranchSwitchFailed,
    Cancelled,
    TimedOut
}

public sealed record RepositoryImportRequest(
    string GitExecutablePath,
    string RepositoryUrl,
    string Branch,
    string WorkspaceDirectory,
    TimeSpan? Timeout = null);

public sealed record RepositoryImportResult(
    RepositoryImportStatus Status,
    string? RepositoryPath = null,
    string? BackupPath = null,
    string? Message = null,
    GitCloneResult? CloneResult = null,
    GitRepositoryRefreshResult? RefreshResult = null,
    GitBranchDiscoveryResult? BranchDiscoveryResult = null,
    GitBranchSwitchResult? BranchSwitchResult = null)
{
    public bool IsSuccess => Status == RepositoryImportStatus.Succeeded;
}

public interface IRepositoryImportCoordinator
{
    Task<RepositoryImportResult> ImportAsync(
        RepositoryImportRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class RepositoryImportCoordinator : IRepositoryImportCoordinator
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(15);

    private readonly IGitCloneService _cloneService;
    private readonly IGitRepositoryRefreshService _refreshService;
    private readonly IGitBranchService _branchService;
    private readonly IGitBranchSwitcher _branchSwitcher;

    public RepositoryImportCoordinator(
        IGitCloneService cloneService,
        IGitRepositoryRefreshService refreshService,
        IGitBranchService branchService,
        IGitBranchSwitcher branchSwitcher)
    {
        _cloneService = cloneService ?? throw new ArgumentNullException(nameof(cloneService));
        _refreshService = refreshService ?? throw new ArgumentNullException(nameof(refreshService));
        _branchService = branchService ?? throw new ArgumentNullException(nameof(branchService));
        _branchSwitcher = branchSwitcher ?? throw new ArgumentNullException(nameof(branchSwitcher));
    }

    public async Task<RepositoryImportResult> ImportAsync(
        RepositoryImportRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (cancellationToken.IsCancellationRequested)
        {
            return Failure(RepositoryImportStatus.Cancelled, "Repository import was cancelled before it started.");
        }

        if (string.IsNullOrWhiteSpace(request.GitExecutablePath) ||
            string.IsNullOrWhiteSpace(request.RepositoryUrl) ||
            string.IsNullOrWhiteSpace(request.Branch) ||
            string.IsNullOrWhiteSpace(request.WorkspaceDirectory))
        {
            return Failure(
                RepositoryImportStatus.InvalidRequest,
                "Git executable, repository URL, branch, and workspace are required.");
        }

        var timeout = request.Timeout ?? DefaultTimeout;
        if (timeout <= TimeSpan.Zero)
        {
            return Failure(RepositoryImportStatus.InvalidRequest, "Import timeout must be greater than zero.");
        }

        var cloneResult = await _cloneService.CloneAsync(
            new GitCloneRequest(
                request.GitExecutablePath.Trim(),
                request.RepositoryUrl.Trim(),
                request.WorkspaceDirectory.Trim(),
                Timeout: timeout),
            progress,
            cancellationToken).ConfigureAwait(false);

        string? repositoryPath;
        string? backupPath = null;
        GitRepositoryRefreshResult? refreshResult = null;

        if (cloneResult.IsSuccess)
        {
            repositoryPath = cloneResult.RepositoryPath;
        }
        else if (cloneResult.Status == GitCloneStatus.TargetNotEmpty &&
                 !string.IsNullOrWhiteSpace(cloneResult.RepositoryPath))
        {
            repositoryPath = cloneResult.RepositoryPath;
            var gitMetadataPath = Path.Combine(repositoryPath, ".git");
            if (!Directory.Exists(gitMetadataPath) && !File.Exists(gitMetadataPath))
            {
                return new RepositoryImportResult(
                    RepositoryImportStatus.ExistingTargetIsNotGit,
                    repositoryPath,
                    Message: "The target folder already contains files but is not a Git repository. It was not modified.",
                    CloneResult: cloneResult);
            }

            refreshResult = await _refreshService.RefreshAsync(
                new GitRepositoryRefreshRequest(
                    request.GitExecutablePath.Trim(),
                    request.RepositoryUrl.Trim(),
                    repositoryPath,
                    timeout),
                progress,
                cancellationToken).ConfigureAwait(false);

            if (!refreshResult.IsSuccess)
            {
                return new RepositoryImportResult(
                    MapRefreshStatus(refreshResult.Status),
                    repositoryPath,
                    refreshResult.BackupPath,
                    refreshResult.Message ?? "Existing repository refresh failed.",
                    cloneResult,
                    refreshResult);
            }

            backupPath = refreshResult.BackupPath;
        }
        else
        {
            return new RepositoryImportResult(
                MapCloneStatus(cloneResult.Status),
                cloneResult.RepositoryPath,
                Message: cloneResult.Message ?? "Repository clone failed.",
                CloneResult: cloneResult);
        }

        if (string.IsNullOrWhiteSpace(repositoryPath))
        {
            return new RepositoryImportResult(
                RepositoryImportStatus.CloneFailed,
                Message: "Repository operation completed without a repository path.",
                CloneResult: cloneResult,
                RefreshResult: refreshResult);
        }

        var branchResult = await _branchService.GetBranchesAsync(
            new GitBranchDiscoveryRequest(
                request.GitExecutablePath.Trim(),
                repositoryPath,
                RefreshRemotes: true,
                Timeout: timeout),
            progress,
            cancellationToken).ConfigureAwait(false);

        if (!branchResult.IsSuccess)
        {
            return new RepositoryImportResult(
                MapBranchDiscoveryStatus(branchResult.Status),
                repositoryPath,
                backupPath,
                branchResult.Message ?? "Branch discovery failed.",
                cloneResult,
                refreshResult,
                branchResult);
        }

        var requestedBranch = request.Branch.Trim();
        var selectedBranch = SelectBranch(branchResult.Branches, requestedBranch);
        if (selectedBranch is null)
        {
            return new RepositoryImportResult(
                RepositoryImportStatus.BranchNotFound,
                repositoryPath,
                backupPath,
                $"Branch '{requestedBranch}' was not found in local or remote refs.",
                cloneResult,
                refreshResult,
                branchResult);
        }

        if (selectedBranch.Kind == GitBranchKind.Local && selectedBranch.IsCurrent)
        {
            return new RepositoryImportResult(
                RepositoryImportStatus.Succeeded,
                repositoryPath,
                backupPath,
                backupPath is null
                    ? $"Repository imported on branch '{selectedBranch.Name}'."
                    : $"Repository refreshed and imported on branch '{selectedBranch.Name}'. Backup: {backupPath}",
                cloneResult,
                refreshResult,
                branchResult);
        }

        var switchResult = await _branchSwitcher.SwitchAsync(
            new GitBranchSwitchRequest(
                request.GitExecutablePath.Trim(),
                repositoryPath,
                selectedBranch,
                timeout),
            progress,
            cancellationToken).ConfigureAwait(false);

        if (!switchResult.IsSuccess)
        {
            return new RepositoryImportResult(
                MapSwitchStatus(switchResult.Status),
                repositoryPath,
                backupPath,
                switchResult.Message ?? "Branch switch failed.",
                cloneResult,
                refreshResult,
                branchResult,
                switchResult);
        }

        return new RepositoryImportResult(
            RepositoryImportStatus.Succeeded,
            repositoryPath,
            backupPath,
            backupPath is null
                ? $"Repository imported on branch '{switchResult.CurrentBranch ?? selectedBranch.Name}'."
                : $"Repository refreshed and imported on branch '{switchResult.CurrentBranch ?? selectedBranch.Name}'. Backup: {backupPath}",
            cloneResult,
            refreshResult,
            branchResult,
            switchResult);
    }

    private static GitBranchInfo? SelectBranch(
        IReadOnlyList<GitBranchInfo> branches,
        string requestedBranch)
    {
        var local = branches.FirstOrDefault(branch =>
            branch.Kind == GitBranchKind.Local &&
            string.Equals(branch.Name, requestedBranch, StringComparison.Ordinal));
        if (local is not null)
        {
            return local;
        }

        var remoteMatches = branches
            .Where(branch => branch.Kind == GitBranchKind.Remote)
            .Where(branch =>
                string.Equals(branch.Name, requestedBranch, StringComparison.Ordinal) ||
                string.Equals(
                    $"{branch.RemoteName}/{branch.Name}",
                    requestedBranch,
                    StringComparison.Ordinal))
            .OrderByDescending(branch =>
                string.Equals(branch.RemoteName, "origin", StringComparison.Ordinal))
            .ThenBy(branch => branch.RemoteName, StringComparer.Ordinal)
            .ToArray();

        return remoteMatches.FirstOrDefault();
    }

    private static RepositoryImportResult Failure(RepositoryImportStatus status, string message)
        => new(status, Message: message);

    private static RepositoryImportStatus MapCloneStatus(GitCloneStatus status)
        => status switch
        {
            GitCloneStatus.Cancelled => RepositoryImportStatus.Cancelled,
            GitCloneStatus.TimedOut => RepositoryImportStatus.TimedOut,
            _ => RepositoryImportStatus.CloneFailed
        };

    private static RepositoryImportStatus MapRefreshStatus(GitRepositoryRefreshStatus status)
        => status switch
        {
            GitRepositoryRefreshStatus.Cancelled => RepositoryImportStatus.Cancelled,
            GitRepositoryRefreshStatus.TimedOut => RepositoryImportStatus.TimedOut,
            _ => RepositoryImportStatus.RefreshFailed
        };

    private static RepositoryImportStatus MapBranchDiscoveryStatus(GitBranchDiscoveryStatus status)
        => status switch
        {
            GitBranchDiscoveryStatus.Cancelled => RepositoryImportStatus.Cancelled,
            GitBranchDiscoveryStatus.TimedOut => RepositoryImportStatus.TimedOut,
            _ => RepositoryImportStatus.BranchDiscoveryFailed
        };

    private static RepositoryImportStatus MapSwitchStatus(GitBranchSwitchStatus status)
        => status switch
        {
            GitBranchSwitchStatus.Cancelled => RepositoryImportStatus.Cancelled,
            GitBranchSwitchStatus.TimedOut => RepositoryImportStatus.TimedOut,
            _ => RepositoryImportStatus.BranchSwitchFailed
        };
}
