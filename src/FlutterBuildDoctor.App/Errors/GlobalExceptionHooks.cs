using System.Windows;
using System.Windows.Threading;
using FlutterBuildDoctor.Application.Errors;

namespace FlutterBuildDoctor.App.Errors;

public sealed class GlobalExceptionHooks : IDisposable
{
    private readonly IAppExceptionReporter _reporter;
    private System.Windows.Application? _application;
    private bool _attached;

    public GlobalExceptionHooks(IAppExceptionReporter reporter)
    {
        _reporter = reporter ?? throw new ArgumentNullException(nameof(reporter));
    }

    public void Attach(System.Windows.Application application)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (_attached)
        {
            return;
        }

        _application = application;
        _application.DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        _attached = true;
    }

    public void Detach()
    {
        if (!_attached)
        {
            return;
        }

        if (_application is not null)
        {
            _application.DispatcherUnhandledException -= OnDispatcherUnhandledException;
        }

        AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        _application = null;
        _attached = false;
    }

    public void Dispose() => Detach();

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _reporter.Report(e.Exception, AppExceptionSource.Dispatcher);
        e.Handled = IsRecoverable(e.Exception);
    }

    private void OnAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception
            ?? new InvalidOperationException("A non-Exception object reached the AppDomain unhandled-exception boundary.");

        _reporter.Report(exception, AppExceptionSource.AppDomain, e.IsTerminating);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _reporter.Report(e.Exception, AppExceptionSource.UnobservedTask);
        e.SetObserved();
    }

    private static bool IsRecoverable(Exception exception) =>
        exception is not OutOfMemoryException
        and not AccessViolationException
        and not StackOverflowException;
}
