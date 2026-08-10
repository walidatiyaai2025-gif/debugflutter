using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Flutter.Commands;

public sealed class FlutterCommandService : IFlutterCommandService
{
    private readonly IProcessRunner _processRunner;
    private readonly IFlutterCommandBuilder _commandBuilder;

    public FlutterCommandService(IProcessRunner processRunner, IFlutterCommandBuilder commandBuilder)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _commandBuilder = commandBuilder ?? throw new ArgumentNullException(nameof(commandBuilder));
    }

    public Task<FlutterCommandExecution> PubGetAsync(
        FlutterCommandContext context,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(FlutterCommandOperation.PubGet, context, progress, cancellationToken);

    public Task<FlutterCommandExecution> CleanAsync(
        FlutterCommandContext context,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(FlutterCommandOperation.Clean, context, progress, cancellationToken);

    public async Task<FlutterAnalyzeExecution> AnalyzeAsync(
        FlutterCommandContext context,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var execution = await ExecuteAsync(
            FlutterCommandOperation.Analyze,
            context,
            progress,
            cancellationToken).ConfigureAwait(false);

        return new FlutterAnalyzeExecution(execution, SummarizeAnalyze(execution.ProcessResult.Output));
    }

    public async Task<FlutterTestExecution> TestAsync(
        FlutterCommandContext context,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var execution = await ExecuteAsync(
            FlutterCommandOperation.Test,
            context,
            progress,
            cancellationToken).ConfigureAwait(false);
        return new FlutterTestExecution(execution);
    }

    public Task<FlutterCommandExecution> PubOutdatedAsync(
        FlutterCommandContext context,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(FlutterCommandOperation.PubOutdated, context, progress, cancellationToken);

    public Task<FlutterCommandExecution> DevicesAsync(
        FlutterCommandContext context,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(FlutterCommandOperation.Devices, context, progress, cancellationToken);

    public Task<FlutterCommandExecution> EmulatorsAsync(
        FlutterCommandContext context,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(FlutterCommandOperation.Emulators, context, progress, cancellationToken);

    public async Task<FlutterCommandExecution> RunAsync(
        FlutterRunRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var processRequest = _commandBuilder.BuildRun(request);
        var result = await _processRunner.RunAsync(processRequest, progress, cancellationToken).ConfigureAwait(false);
        return new FlutterCommandExecution(FlutterCommandOperation.Run, result);
    }

    private async Task<FlutterCommandExecution> ExecuteAsync(
        FlutterCommandOperation operation,
        FlutterCommandContext context,
        IProgress<ProcessOutputLine>? progress,
        CancellationToken cancellationToken)
    {
        var request = _commandBuilder.Build(operation, context);
        var result = await _processRunner.RunAsync(request, progress, cancellationToken).ConfigureAwait(false);
        return new FlutterCommandExecution(operation, result);
    }

    private static FlutterAnalyzeSummary SummarizeAnalyze(IReadOnlyList<ProcessOutputLine> output)
    {
        var info = 0;
        var warnings = 0;
        var errors = 0;

        foreach (var line in output)
        {
            var text = line.Text.TrimStart();
            if (text.StartsWith("info •", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("info -", StringComparison.OrdinalIgnoreCase))
            {
                info++;
            }
            else if (text.StartsWith("warning •", StringComparison.OrdinalIgnoreCase) ||
                     text.StartsWith("warning -", StringComparison.OrdinalIgnoreCase))
            {
                warnings++;
            }
            else if (text.StartsWith("error •", StringComparison.OrdinalIgnoreCase) ||
                     text.StartsWith("error -", StringComparison.OrdinalIgnoreCase))
            {
                errors++;
            }
        }

        return new FlutterAnalyzeSummary(info, warnings, errors);
    }
}
