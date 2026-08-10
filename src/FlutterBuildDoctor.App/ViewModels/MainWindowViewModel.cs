using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlutterBuildDoctor.App.Services;
using FlutterBuildDoctor.Application.Errors;

namespace FlutterBuildDoctor.App.ViewModels;

public enum ShellPage
{
    Dashboard = 0,
    Projects,
    EnvironmentDoctor
}

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IAppExceptionReporter _exceptionReporter;
    private readonly SynchronizationContext? _uiContext;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private ShellPage _currentPage = ShellPage.Dashboard;

    public MainWindowViewModel(
        IAppExceptionReporter exceptionReporter,
        ProjectHeaderViewModel projectHeader,
        RepositoryManagerViewModel repositoryManager,
        EnvironmentDoctorViewModel environmentDoctor,
        IApplicationIdentityService? applicationIdentityService = null)
    {
        _exceptionReporter = exceptionReporter ?? throw new ArgumentNullException(nameof(exceptionReporter));
        ProjectHeader = projectHeader ?? throw new ArgumentNullException(nameof(projectHeader));
        RepositoryManager = repositoryManager ?? throw new ArgumentNullException(nameof(repositoryManager));
        EnvironmentDoctor = environmentDoctor ?? throw new ArgumentNullException(nameof(environmentDoctor));
        ApplicationIdentity = applicationIdentityService?.Current
            ?? new ApplicationIdentity("development", "local", null);
        _uiContext = SynchronizationContext.Current;
        _exceptionReporter.ExceptionReported += OnExceptionReported;

        if (_exceptionReporter.Latest is { } latest)
        {
            _statusMessage = latest.UserMessage;
        }
    }

    public ProjectHeaderViewModel ProjectHeader { get; }

    public RepositoryManagerViewModel RepositoryManager { get; }

    public EnvironmentDoctorViewModel EnvironmentDoctor { get; }

    public ApplicationIdentity ApplicationIdentity { get; }

    public string ApplicationIdentityText => ApplicationIdentity.DisplayText;

    public bool IsDashboardVisible => CurrentPage == ShellPage.Dashboard;

    public bool IsProjectsVisible => CurrentPage == ShellPage.Projects;

    public bool IsEnvironmentDoctorVisible => CurrentPage == ShellPage.EnvironmentDoctor;

    public string ApplicationName => "Flutter Build Doctor";

    public string StartupStatus => "Ready";

    partial void OnCurrentPageChanged(ShellPage value)
    {
        OnPropertyChanged(nameof(IsDashboardVisible));
        OnPropertyChanged(nameof(IsProjectsVisible));
        OnPropertyChanged(nameof(IsEnvironmentDoctorVisible));
    }

    [RelayCommand]
    private void ShowDashboard()
    {
        CurrentPage = ShellPage.Dashboard;
        StatusMessage = "Dashboard";
    }

    [RelayCommand]
    private void ShowProjects()
    {
        CurrentPage = ShellPage.Projects;
        StatusMessage = "Projects";
    }

    [RelayCommand]
    private async Task ShowEnvironmentDoctorAsync()
    {
        CurrentPage = ShellPage.EnvironmentDoctor;
        StatusMessage = "Environment Doctor";

        if (!EnvironmentDoctor.HasScanned && !EnvironmentDoctor.IsBusy)
        {
            await EnvironmentDoctor.ScanCommand.ExecuteAsync(null);
        }
    }

    public void Dispose()
    {
        _exceptionReporter.ExceptionReported -= OnExceptionReported;
        EnvironmentDoctor.Dispose();
    }

    private void OnExceptionReported(AppExceptionRecord record)
    {
        if (_uiContext is null || ReferenceEquals(SynchronizationContext.Current, _uiContext))
        {
            StatusMessage = record.UserMessage;
            return;
        }

        _uiContext.Post(_ => StatusMessage = record.UserMessage, null);
    }
}
