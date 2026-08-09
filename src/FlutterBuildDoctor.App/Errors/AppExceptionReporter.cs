using FlutterBuildDoctor.Application.Errors;
using Microsoft.Extensions.Logging;

namespace FlutterBuildDoctor.App.Errors;

public sealed class AppExceptionReporter : IAppExceptionReporter
{
    private readonly ILogger<AppExceptionReporter> _logger;
    private AppExceptionRecord? _latest;

    public AppExceptionReporter(ILogger<AppExceptionReporter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public event Action<AppExceptionRecord>? ExceptionReported;

    public AppExceptionRecord? Latest => Volatile.Read(ref _latest);

    public AppExceptionRecord Report(
        Exception exception,
        AppExceptionSource source,
        bool isTerminating = false)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var id = Guid.NewGuid();
        var record = new AppExceptionRecord(
            id,
            DateTimeOffset.UtcNow,
            source,
            isTerminating
                ? $"A critical error was captured. Reference: {id:N}"
                : $"An unexpected error was captured. Reference: {id:N}",
            exception.GetType().FullName ?? exception.GetType().Name,
            isTerminating);

        Volatile.Write(ref _latest, record);

        var level = isTerminating ? LogLevel.Critical : LogLevel.Error;
        _logger.Log(
            level,
            "Unhandled exception {ExceptionId} from {ExceptionSource}. Type={ExceptionType}; HResult={HResult}; StackTrace={StackTrace}",
            record.Id,
            record.Source,
            record.ExceptionType,
            exception.HResult,
            exception.StackTrace);

        ExceptionReported?.Invoke(record);
        return record;
    }
}
