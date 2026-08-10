using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Flutter.Commands;

public enum FlutterCommandKind
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

public sealed record FlutterRunRequest(
    string? DeviceId = null,
    string? Flavor = null,
    string? Target = null,
    bool Debug = true,
    IReadOnlyList<string>? DartDefines = null);

public sealed record FlutterCommandRequest(
    FlutterCommandKind Kind,
    string FlutterExecutable,
    string? WorkingDirectory = null,
    FlutterRunRequest? Run = null,
    TimeSpan? Timeout = null);

public sealed record FlutterAnalyzeSummary(int IssueCount, int ErrorCount, int WarningCount, int InfoCount);
public sealed record FlutterTestSummary(int Passed, int Failed, int Skipped);

public sealed record FlutterCommandResult(
    FlutterCommandKind Kind,
    ProcessResult Process,
    FlutterAnalyzeSummary? Analyze = null,
    FlutterTestSummary? Tests = null)
{
    public bool IsSuccess => Process.IsSuccess;
    public bool IsCancelled => Process.Status == ProcessExecutionStatus.Cancelled;
    public bool IsTimedOut => Process.Status == ProcessExecutionStatus.TimedOut;
}

public interface IFlutterCommandBuilder
{
    ProcessRequest Build(FlutterCommandRequest request);
}

public sealed class FlutterCommandBuilder : IFlutterCommandBuilder
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    public ProcessRequest Build(FlutterCommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FlutterExecutable);

        var arguments = request.Kind switch
        {
            FlutterCommandKind.PubGet => new[] { "pub", "get" },
            FlutterCommandKind.Clean => new[] { "clean" },
            FlutterCommandKind.Analyze => new[] { "analyze" },
            FlutterCommandKind.Test => new[] { "test" },
            FlutterCommandKind.PubOutdated => new[] { "pub", "outdated" },
            FlutterCommandKind.Devices => new[] { "devices" },
            FlutterCommandKind.Emulators => new[] { "emulators" },
            FlutterCommandKind.Run => BuildRunArguments(request.Run ?? new FlutterRunRequest()),
            _ => throw new ArgumentOutOfRangeException(nameof(request.Kind), request.Kind, null)
        };

        return new ProcessRequest(
            request.FlutterExecutable,
            arguments,
            request.WorkingDirectory,
            Timeout: request.Timeout ?? DefaultTimeout,
            DisplayName: $"flutter {string.Join(' ', arguments)}");
    }

    private static IReadOnlyList<string> BuildRunArguments(FlutterRunRequest request)
    {
        var arguments = new List<string> { "run" };
        if (!string.IsNullOrWhiteSpace(request.DeviceId))
        {
            arguments.Add("-d");
            arguments.Add(request.DeviceId.Trim());
        }
        if (!string.IsNullOrWhiteSpace(request.Flavor))
        {
            arguments.Add("--flavor");
            arguments.Add(request.Flavor.Trim());
        }
        if (!string.IsNullOrWhiteSpace(request.Target))
        {
            arguments.Add("--target");
            arguments.Add(request.Target.Trim());
        }
        arguments.Add(request.Debug ? "--debug" : "--profile");

        if (request.DartDefines is not null)
        {
            foreach (var define in request.DartDefines.Where(static value => !string.IsNullOrWhiteSpace(value)))
            {
                arguments.Add("--dart-define");
                arguments.Add(define.Trim());
            }
        }
        return arguments;
    }
}

public interface IFlutterCommandCenter
{
    Task<FlutterCommandResult> ExecuteAsync(
        FlutterCommandRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default);

    Task<FlutterCommandResult> PubGetAsync(string flutterExecutable, string workingDirectory, IProgress<ProcessOutputLine>? progress = null, CancellationToken cancellationToken = default);
    Task<FlutterCommandResult> CleanAsync(string flutterExecutable, string workingDirectory, IProgress<ProcessOutputLine>? progress = null, CancellationToken cancellationToken = default);
    Task<FlutterCommandResult> AnalyzeAsync(string flutterExecutable, string workingDirectory, IProgress<ProcessOutputLine>? progress = null, CancellationToken cancellationToken = default);
    Task<FlutterCommandResult> TestAsync(string flutterExecutable, string workingDirectory, IProgress<ProcessOutputLine>? progress = null, CancellationToken cancellationToken = default);
    Task<FlutterCommandResult> PubOutdatedAsync(string flutterExecutable, string workingDirectory, IProgress<ProcessOutputLine>? progress = null, CancellationToken cancellationToken = default);
    Task<FlutterCommandResult> DevicesAsync(string flutterExecutable, IProgress<ProcessOutputLine>? progress = null, CancellationToken cancellationToken = default);
    Task<FlutterCommandResult> EmulatorsAsync(string flutterExecutable, IProgress<ProcessOutputLine>? progress = null, CancellationToken cancellationToken = default);
    Task<FlutterCommandResult> RunAsync(string flutterExecutable, string workingDirectory, FlutterRunRequest run, IProgress<ProcessOutputLine>? progress = null, CancellationToken cancellationToken = default);
}

public sealed class FlutterCommandCenter : IFlutterCommandCenter
{
    private readonly IProcessRunner _processRunner;
    private readonly IFlutterCommandBuilder _builder;

    public FlutterCommandCenter(IProcessRunner processRunner, IFlutterCommandBuilder builder)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
    }

    public async Task<FlutterCommandResult> ExecuteAsync(
        FlutterCommandRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var processRequest = _builder.Build(request);
        var process = await _processRunner.RunAsync(processRequest, progress, cancellationToken).ConfigureAwait(false);
        return new FlutterCommandResult(
            request.Kind,
            process,
            request.Kind == FlutterCommandKind.Analyze ? ParseAnalyze(process.Output) : null,
            request.Kind == FlutterCommandKind.Test ? ParseTests(process.Output) : null);
    }

    public Task<FlutterCommandResult> PubGetAsync(string flutterExecutable, string workingDirectory, IProgress<ProcessOutputLine>? progress = null, CancellationToken cancellationToken = default)
        => ExecuteAsync(new FlutterCommandRequest(FlutterCommandKind.PubGet, flutterExecutable, workingDirectory), progress, cancellationToken);

    public Task<FlutterCommandResult> CleanAsync(string flutterExecutable, string workingDirectory, IProgress<ProcessOutputLine>? progress = null, CancellationToken cancellationToken = default)
        => ExecuteAsync(new FlutterCommandRequest(FlutterCommandKind.Clean, flutterExecutable, workingDirectory), progress, cancellationToken);

    public Task<FlutterCommandResult> AnalyzeAsync(string flutterExecutable, string workingDirectory, IProgress<ProcessOutputLine>? progress = null, CancellationToken cancellationToken = default)
        => ExecuteAsync(new FlutterCommandRequest(FlutterCommandKind.Analyze, flutterExecutable, workingDirectory), progress, cancellationToken);

    public Task<FlutterCommandResult> TestAsync(string flutterExecutable, string workingDirectory, IProgress<ProcessOutputLine>? progress = null, CancellationToken cancellationToken = default)
        => ExecuteAsync(new FlutterCommandRequest(FlutterCommandKind.Test, flutterExecutable, workingDirectory), progress, cancellationToken);

    public Task<FlutterCommandResult> PubOutdatedAsync(string flutterExecutable, string workingDirectory, IProgress<ProcessOutputLine>? progress = null, CancellationToken cancellationToken = default)
        => ExecuteAsync(new FlutterCommandRequest(FlutterCommandKind.PubOutdated, flutterExecutable, workingDirectory), progress, cancellationToken);

    public Task<FlutterCommandResult> DevicesAsync(string flutterExecutable, IProgress<ProcessOutputLine>? progress = null, CancellationToken cancellationToken = default)
        => ExecuteAsync(new FlutterCommandRequest(FlutterCommandKind.Devices, flutterExecutable), progress, cancellationToken);

    public Task<FlutterCommandResult> EmulatorsAsync(string flutterExecutable, IProgress<ProcessOutputLine>? progress = null, CancellationToken cancellationToken = default)
        => ExecuteAsync(new FlutterCommandRequest(FlutterCommandKind.Emulators, flutterExecutable), progress, cancellationToken);

    public Task<FlutterCommandResult> RunAsync(string flutterExecutable, string workingDirectory, FlutterRunRequest run, IProgress<ProcessOutputLine>? progress = null, CancellationToken cancellationToken = default)
        => ExecuteAsync(new FlutterCommandRequest(FlutterCommandKind.Run, flutterExecutable, workingDirectory, run), progress, cancellationToken);

    private static FlutterAnalyzeSummary ParseAnalyze(IReadOnlyList<ProcessOutputLine> output)
    {
        var issueCount = 0;
        var errors = 0;
        var warnings = 0;
        var infos = 0;
        foreach (var text in output.Select(static line => line.Text.Trim()))
        {
            if (text.StartsWith("error", StringComparison.OrdinalIgnoreCase)) { errors++; issueCount++; }
            else if (text.StartsWith("warning", StringComparison.OrdinalIgnoreCase)) { warnings++; issueCount++; }
            else if (text.StartsWith("info", StringComparison.OrdinalIgnoreCase)) { infos++; issueCount++; }
        }
        return new FlutterAnalyzeSummary(issueCount, errors, warnings, infos);
    }

    private static FlutterTestSummary ParseTests(IReadOnlyList<ProcessOutputLine> output)
    {
        var passed = 0;
        var failed = 0;
        var skipped = 0;
        foreach (var text in output.Select(static line => line.Text))
        {
            if (text.Contains("All tests passed", StringComparison.OrdinalIgnoreCase)) passed = Math.Max(passed, 1);
            if (text.Contains("Some tests failed", StringComparison.OrdinalIgnoreCase) || text.Contains("FAILED", StringComparison.OrdinalIgnoreCase)) failed++;
            if (text.Contains("skipped", StringComparison.OrdinalIgnoreCase)) skipped++;
        }
        return new FlutterTestSummary(passed, failed, skipped);
    }
}
