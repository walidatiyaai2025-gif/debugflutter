using CommunityToolkit.Mvvm.ComponentModel;
using FlutterBuildDoctor.Git.Repository;

namespace FlutterBuildDoctor.App.ViewModels;

public sealed partial class ProjectHeaderViewModel : ObservableObject
{
    private readonly IGitRepositoryIdentityService _identityService;

    [ObservableProperty]
    private bool _hasProject;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _projectName = "No project selected";

    [ObservableProperty]
    private string _repositoryPath = "Import a Flutter repository to begin diagnostics";

    [ObservableProperty]
    private string _branchText = "Branch: —";

    [ObservableProperty]
    private string _commitText = "Commit: —";

    [ObservableProperty]
    private string _remoteText = "Remote: —";

    [ObservableProperty]
    private string _identityStatus = "No Git identity loaded";

    public ProjectHeaderViewModel(IGitRepositoryIdentityService identityService)
    {
        _identityService = identityService ?? throw new ArgumentNullException(nameof(identityService));
    }

    public async Task<GitRepositoryIdentityResult> LoadAsync(
        string gitExecutablePath,
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var result = await _identityService.ReadAsync(
                new GitRepositoryIdentityRequest(gitExecutablePath, repositoryPath),
                cancellationToken: cancellationToken);

            if (result.IsSuccess && result.Identity is { } identity)
            {
                ApplyIdentity(identity);
                IdentityStatus = result.Message ?? "Git identity loaded.";
            }
            else
            {
                ApplyUnavailable(repositoryPath, result.Message ?? "Git identity is unavailable.");
            }

            return result;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void Clear()
    {
        HasProject = false;
        ProjectName = "No project selected";
        RepositoryPath = "Import a Flutter repository to begin diagnostics";
        BranchText = "Branch: —";
        CommitText = "Commit: —";
        RemoteText = "Remote: —";
        IdentityStatus = "No Git identity loaded";
    }

    private void ApplyIdentity(GitRepositoryIdentity identity)
    {
        HasProject = true;
        ProjectName = GetProjectName(identity.RepositoryPath);
        RepositoryPath = identity.RepositoryPath;
        BranchText = identity.IsDetached
            ? "Branch: detached HEAD"
            : $"Branch: {identity.BranchName ?? "—"}";
        CommitText = $"Commit: {identity.CommitSha}";
        RemoteText = BuildRemoteText(identity);
    }

    private void ApplyUnavailable(string repositoryPath, string message)
    {
        HasProject = !string.IsNullOrWhiteSpace(repositoryPath);
        ProjectName = HasProject ? GetProjectName(repositoryPath) : "No project selected";
        RepositoryPath = string.IsNullOrWhiteSpace(repositoryPath)
            ? "Import a Flutter repository to begin diagnostics"
            : repositoryPath;
        BranchText = "Branch: unavailable";
        CommitText = "Commit: unavailable";
        RemoteText = "Remote: unavailable";
        IdentityStatus = message;
    }

    private static string BuildRemoteText(GitRepositoryIdentity identity)
    {
        if (identity.IsDetached)
        {
            return "Remote: —";
        }

        if (string.IsNullOrWhiteSpace(identity.RemoteName))
        {
            return string.IsNullOrWhiteSpace(identity.Upstream)
                ? "Remote: none"
                : $"Remote: — • Upstream: {identity.Upstream}";
        }

        return string.IsNullOrWhiteSpace(identity.Upstream)
            ? $"Remote: {identity.RemoteName}"
            : $"Remote: {identity.RemoteName} • Upstream: {identity.Upstream}";
    }

    private static string GetProjectName(string repositoryPath)
    {
        try
        {
            var fullPath = Path.GetFullPath(repositoryPath);
            var trimmed = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Path.GetFileName(trimmed) is { Length: > 0 } name
                ? name
                : fullPath;
        }
        catch
        {
            return repositoryPath;
        }
    }
}
