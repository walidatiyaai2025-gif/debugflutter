namespace FlutterBuildDoctor.Application.Processes;

public sealed record ProcessExecutionReceipt(
    Guid ExecutionId,
    string DisplayName,
    string SanitizedCommand,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    ProcessExecutionStatus Status,
    int? ExitCode,
    string? FailureReason)
{
    public TimeSpan Duration => FinishedAt - StartedAt;

    public bool IsSuccess => Status == ProcessExecutionStatus.Succeeded;
}
