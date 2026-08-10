using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Flutter.Commands;

public enum FlutterCommandOperation
{
    PubGet = 0,
    Clean,
    Analyze,
    Test,
    PubOutdated,
    Devices,
    Emulators,
    Run
}

public sealed record FlutterCommandContext(
    string FlutterExecutable,
    string WorkingDirectory);

public sealed record FlutterRunRequest(
    FlutterCommandContext Context,
    string DeviceId,
    string? Flavor = null,
    string? Target = null);

public sealed record FlutterCommandExecution(
    FlutterCommandOperation Operation,
    ProcessResult ProcessResult)
{
    public ProcessExecutionStatus Status => ProcessResult.Status;
    public bool IsSuccess => ProcessResult.IsSuccess;
    public TimeSpan Duration => ProcessResult.Duration;
}

public sealed record FlutterAnalyzeSummary(
    int InfoCount,
    int WarningCount,
    int ErrorCount)
{
    public int TotalCount => InfoCount + WarningCount + ErrorCount;
    public bool HasErrors => ErrorCount > 0;
}

public sealed record FlutterAnalyzeExecution(
    FlutterCommandExecution Execution,
    FlutterAnalyzeSummary Summary)
{
    public bool IsSuccess => Execution.IsSuccess && !Summary.HasErrors;
}

public sealed record FlutterTestExecution(FlutterCommandExecution Execution)
{
    public bool Passed => Execution.Status == ProcessExecutionStatus.Succeeded;
    public bool WasCancelled => Execution.Status == ProcessExecutionStatus.Cancelled;
}

public interface IFlutterCommandBuilder
{
    ProcessRequest Build(FlutterCommandOperation operation, FlutterCommandContext context);
    ProcessRequest BuildRun(FlutterRunRequest request);
}

public interface IFlutterCommandService
{
    Task<FlutterCommandExecution> PubGetAsync(
        FlutterCommandContext context,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default);

    Task<FlutterCommandExecution> CleanAsync(
        FlutterCommandContext context,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default);

    Task<FlutterAnalyzeExecution> AnalyzeAsync(
        FlutterCommandContext context,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default);

    Task<FlutterTestExecution> TestAsync(
        FlutterCommandContext context,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default);

    Task<FlutterCommandExecution> PubOutdatedAsync(
        FlutterCommandContext context,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default);

    Task<FlutterCommandExecution> DevicesAsync(
        FlutterCommandContext context,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default);

    Task<FlutterCommandExecution> EmulatorsAsync(
        FlutterCommandContext context,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default);

    Task<FlutterCommandExecution> RunAsync(
        FlutterRunRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default);
}
