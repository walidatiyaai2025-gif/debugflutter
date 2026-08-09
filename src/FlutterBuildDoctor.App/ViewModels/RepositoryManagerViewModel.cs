using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlutterBuildDoctor.App.Services;
using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Git.Branches;

namespace FlutterBuildDoctor.App.ViewModels;

public sealed partial class RepositoryManagerViewModel : ObservableObject, IDisposable
{
    private readonly IGitExecutableResolver _gitResolver;
    private readonly IRepositoryImportCoordinator _importCoordinator;
    private readonly IGitPullService _gitPullService;
    private readonly ProjectHeaderViewModel _projectHeader;
    private CancellationTokenSource? _operationCancellation;

    [ObservableProperty]
    private string _repositoryUrl = string.Empty;

    [ObservableProperty]
    private string _branch = "main";

    [ObservableProperty]
    private string _workspaceDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "source",
        "repos");

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Enter a Git repository, branch, and workspace to import a project.";

    [ObservableProperty]
    private string? _repositoryPath;

    [ObservableProperty]
    private string? _lastBackupPath;

    [ObservableProperty]
    private string _gitStatus = "Git: not checked";

    public RepositoryManagerViewModel(
        IGitExecutableResolver gitResolver,
        IRepositoryImportCoordinator importCoordinator,
        IGitPullService gitPullService,
        ProjectHeaderViewModel projectHeader)
    {
        _gitResolver = gitResolver ?? throw new ArgumentNullException(nameof(gitResolver));
        _importCoordinator = importCoordinator ?? throw new ArgumentNullException(nameof(importCoordinator));
        _gitPullService = gitPullService ?? throw new ArgumentNullException(nameof(gitPullService));
        _projectHeader = projectHeader ?? throw new ArgumentNullException(nameof(projectHeader));
    }

    public ObservableCollection<string> Activity { get; } = new();

    public bool CanEdit => !IsBusy;

    public bool CanCancel => IsBusy;

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanCancel));
        ImportCommand.NotifyCanExecuteChanged();
        PullCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    partial void OnRepositoryPathChanged(string? value)
        => PullCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        Activity.Clear();
        LastBackupPath = null;
        RepositoryPath = null;
        StatusMessage = "Checking Git...";
        _operationCancellation = new CancellationTokenSource();

        try
        {
            var git = await _gitResolver.ResolveAsync(_operationCancellation.Token);
            GitStatus = git.IsAvailable
                ? $"Git: {git.Version ?? "available"} • {git.Path}"
                : $"Git: unavailable • {git.Message}";

            if (!git.IsAvailable || string.IsNullOrWhiteSpace(git.Path))
            {
                StatusMessage = git.Message;
                Activity.Add(git.Message);
                return;
            }

            StatusMessage = "Importing repository...";
            var result = await _importCoordinator.ImportAsync(
                new RepositoryImportRequest(
                    git.Path,
                    RepositoryUrl,
                    Branch,
                    WorkspaceDirectory),
                CreateActivityProgress(),
                _operationCancellation.Token);

            StatusMessage = result.Message ?? result.Status.ToString();
            RepositoryPath = result.RepositoryPath;
            LastBackupPath = result.BackupPath;

            if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.RepositoryPath))
            {
                Activity.Add(StatusMessage);
                return;
            }

            var identity = await _projectHeader.LoadAsync(
                git.Path,
                result.RepositoryPath,
                _operationCancellation.Token);

            if (!identity.IsSuccess)
            {
                StatusMessage = $"Repository imported, but Git identity could not be loaded: {identity.Message}";
                Activity.Add(StatusMessage);
                return;
            }

            Activity.Add(StatusMessage);
            if (!string.IsNullOrWhiteSpace(LastBackupPath))
            {
                Activity.Add($"Backup preserved: {LastBackupPath}");
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Repository import cancelled.";
            Activity.Add(StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Repository import failed unexpectedly: {ex.Message}";
            Activity.Add(StatusMessage);
        }
        finally
        {
            FinishOperation();
        }
    }

    private bool CanImport()
        => !IsBusy &&
           !string.IsNullOrWhiteSpace(RepositoryUrl) &&
           !string.IsNullOrWhiteSpace(Branch) &&
           !string.IsNullOrWhiteSpace(WorkspaceDirectory);

    [RelayCommand(CanExecute = nameof(CanPull))]
    private async Task PullAsync()
    {
        var repositoryPath = RepositoryPath;
        if (IsBusy || string.IsNullOrWhiteSpace(repositoryPath))
        {
            return;
        }

        IsBusy = true;
        Activity.Clear();
        StatusMessage = "Checking Git before pull...";
        _operationCancellation = new CancellationTokenSource();

        try
        {
            var git = await _gitResolver.ResolveAsync(_operationCancellation.Token);
            GitStatus = git.IsAvailable
                ? $"Git: {git.Version ?? "available"} • {git.Path}"
                : $"Git: unavailable • {git.Message}";

            if (!git.IsAvailable || string.IsNullOrWhiteSpace(git.Path))
            {
                StatusMessage = git.Message;
                Activity.Add(git.Message);
                return;
            }

            StatusMessage = "Pulling current branch with fast-forward-only safety...";
            var result = await _gitPullService.PullAsync(
                new GitPullRequest(git.Path, repositoryPath),
                CreateActivityProgress(),
                _operationCancellation.Token);

            StatusMessage = result.Message ?? result.Status.ToString();
            Activity.Add(StatusMessage);

            if (!result.IsSuccess)
            {
                return;
            }

            var identity = await _projectHeader.LoadAsync(
                git.Path,
                repositoryPath,
                _operationCancellation.Token);

            if (!identity.IsSuccess)
            {
                StatusMessage = $"Pull succeeded, but Git identity refresh failed: {identity.Message}";
                Activity.Add(StatusMessage);
                return;
            }

            StatusMessage = result.Changed
                ? $"Pull completed. Updated to {ShortSha(result.AfterCommitSha)}."
                : "Pull completed. Current branch is already up to date.";
            Activity.Add(StatusMessage);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Git pull cancelled.";
            Activity.Add(StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Git pull failed unexpectedly: {ex.Message}";
            Activity.Add(StatusMessage);
        }
        finally
        {
            FinishOperation();
        }
    }

    private bool CanPull()
        => !IsBusy && !string.IsNullOrWhiteSpace(RepositoryPath);

    partial void OnRepositoryUrlChanged(string value)
        => ImportCommand.NotifyCanExecuteChanged();

    partial void OnBranchChanged(string value)
        => ImportCommand.NotifyCanExecuteChanged();

    partial void OnWorkspaceDirectoryChanged(string value)
        => ImportCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanCancelOperation))]
    private void Cancel()
    {
        if (_operationCancellation is null || _operationCancellation.IsCancellationRequested)
        {
            return;
        }

        StatusMessage = "Cancelling repository operation...";
        _operationCancellation.Cancel();
    }

    private bool CanCancelOperation() => IsBusy;

    private Progress<ProcessOutputLine> CreateActivityProgress()
        => new(line =>
        {
            if (!string.IsNullOrWhiteSpace(line.Text))
            {
                Activity.Add($"[{line.Stream}] {line.Text}");
            }
        });

    private void FinishOperation()
    {
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        IsBusy = false;
    }

    private static string ShortSha(string? sha)
        => string.IsNullOrWhiteSpace(sha)
            ? "latest commit"
            : sha[..Math.Min(8, sha.Length)];

    public void Dispose()
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = null;
    }
}
