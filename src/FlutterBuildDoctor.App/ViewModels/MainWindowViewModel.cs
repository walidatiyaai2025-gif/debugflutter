using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FlutterBuildDoctor.Application.Errors;

namespace FlutterBuildDoctor.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IAppExceptionReporter _exceptionReporter;
    private readonly SynchronizationContext? _uiContext;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    public MainWindowViewModel(
        IAppExceptionReporter exceptionReporter,
        ProjectHeaderViewModel projectHeader,
        RepositoryManagerViewModel repositoryManager)
    {
        _exceptionReporter = exceptionReporter ?? throw new ArgumentNullException(nameof(exceptionReporter));
        ProjectHeader = projectHeader ?? throw new ArgumentNullException(nameof(projectHeader));
        RepositoryManager = repositoryManager ?? throw new ArgumentNullException(nameof(repositoryManager));
        _uiContext = SynchronizationContext.Current;
        _exceptionReporter.ExceptionReported += OnExceptionReported;

        if (_exceptionReporter.Latest is { } latest)
        {
            _statusMessage = latest.UserMessage;
        }
    }

    public ProjectHeaderViewModel ProjectHeader { get; }

    public RepositoryManagerViewModel RepositoryManager { get; }

    public string ApplicationName => "Flutter Build Doctor";

    public string StartupStatus => "Ready";

    public void Dispose()
    {
        _exceptionReporter.ExceptionReported -= OnExceptionReported;
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
