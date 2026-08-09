namespace FlutterBuildDoctor.Application.Errors;

public interface IAppExceptionReporter
{
    event Action<AppExceptionRecord>? ExceptionReported;

    AppExceptionRecord? Latest { get; }

    AppExceptionRecord Report(
        Exception exception,
        AppExceptionSource source,
        bool isTerminating = false);
}
