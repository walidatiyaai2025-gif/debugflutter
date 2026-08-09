namespace FlutterBuildDoctor.Application.Processes;

public enum ProcessStream
{
    StdOut = 0,
    StdErr = 1
}

public enum ProcessExecutionStatus
{
    Created = 0,
    Running,
    Succeeded,
    Failed,
    Cancelled,
    TimedOut
}

public sealed record ProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string?>? Environment = null,
    TimeSpan? Timeout = null,
    string? DisplayName = null,
    bool RedactCommand = false,
    IReadOnlyCollection<string>? SensitiveValues = null);

public sealed record ProcessOutputLine(
    DateTimeOffset Timestamp,
    ProcessStream Stream,
    string Text);

public sealed record ProcessResult(
    ProcessExecutionStatus Status,
    int? ExitCode,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    IReadOnlyList<ProcessOutputLine> Output,
    string SanitizedCommand,
    string? FailureReason = null,
    ProcessExecutionReceipt? ExecutionReceipt = null)
{
    public TimeSpan Duration => FinishedAt - StartedAt;
    public bool IsSuccess => Status == ProcessExecutionStatus.Succeeded;
}

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        ProcessRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default);
}
