namespace FlutterBuildDoctor.Application.Errors;

public sealed record AppExceptionRecord(
    Guid Id,
    DateTimeOffset Timestamp,
    AppExceptionSource Source,
    string UserMessage,
    string ExceptionType,
    bool IsTerminating);
