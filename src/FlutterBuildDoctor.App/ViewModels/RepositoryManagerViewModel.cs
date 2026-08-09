using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlutterBuildDoctor.App.Services;
using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.App.ViewModels;

public sealed partial class RepositoryManagerViewModel : ObservableObject, IDisposable
{
    private readonly IGitExecutableResolver _gitResolver;
    private readonly IRepositoryImportCoordinator _importCoordinator;
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
        ProjectHeaderViewModel projectHeader)
    {
        _gitResolver = gitResolver ?? throw new ArgumentNullException(nameof(gitResolver));
        _importCoordinator = importCoordinator ?? throw new ArgumentNullException(nameof(importCoordinator));
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
        CancelCommand.NotifyCanExecuteChanged();
    }

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
            var progress = new Progress<ProcessOutputLine>(line =>
            {
                if (!string.IsNullOrWhiteSpace(line.Text))
                {
                    Activity.Add($"[{line.Stream}] {line.Text}");
                }
            });

            var result = await _importCoordinator.ImportAsync(
                new RepositoryImportRequest(
                    git.Path,
                    RepositoryUrl,
                    Branch,
                    WorkspaceDirectory),
                progress,
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
            _operationCancellation?.Dispose();
            _operationCancellation = null;
            IsBusy = false;
        }
    }

    private bool CanImport()
        => !IsBusy &&
           !string.IsNullOrWhiteSpace(RepositoryUrl) &&
           !string.IsNullOrWhiteSpace(Branch) &&
           !string.IsNullOrWhiteSpace(WorkspaceDirectory);

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

        StatusMessage = "Cancelling repository import...";
        _operationCancellation.Cancel();
    }

    private bool CanCancelOperation() => IsBusy;

    public void Dispose()
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = null;
    }
}
