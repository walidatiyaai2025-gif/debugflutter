using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Flutter.Commands;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class FlutterCommandServiceTests
{
    private static readonly FlutterCommandContext Context = new("flutter", @"C:\work\app");

    [Fact]
    public async Task AnalyzeAsync_SummarizesStructuredAnalyzerLines()
    {
        var runner = new RecordingProcessRunner(Success(
            "Analyzing app...",
            "   info • Prefer final • lib/a.dart:1:1 • prefer_final_locals",
            "warning • Unused import • lib/b.dart:2:1 • unused_import",
            "  error • Undefined name • lib/c.dart:3:1 • undefined_identifier",
            "3 issues found."));
        var service = new FlutterCommandService(runner, new FlutterCommandBuilder());

        var result = await service.AnalyzeAsync(Context);

        Assert.Equal(1, result.Summary.InfoCount);
        Assert.Equal(1, result.Summary.WarningCount);
        Assert.Equal(1, result.Summary.ErrorCount);
        Assert.Equal(3, result.Summary.TotalCount);
        Assert.True(result.Summary.HasErrors);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task TestAsync_MapsCancellationWithoutConvertingItToFailureOrSuccess()
    {
        var runner = new RecordingProcessRunner(Result(ProcessExecutionStatus.Cancelled, null, "Cancelled by user."));
        var service = new FlutterCommandService(runner, new FlutterCommandBuilder());

        var result = await service.TestAsync(Context);

        Assert.False(result.Passed);
        Assert.True(result.WasCancelled);
        Assert.Equal(ProcessExecutionStatus.Cancelled, result.Execution.Status);
    }

    [Fact]
    public async Task PubGetAsync_ForwardsProgressAndCancellationToken()
    {
        var runner = new RecordingProcessRunner(Success("Resolving dependencies..."));
        var service = new FlutterCommandService(runner, new FlutterCommandBuilder());
        using var source = new CancellationTokenSource();
        IProgress<ProcessOutputLine> progress = new Progress<ProcessOutputLine>(_ => { });

        var result = await service.PubGetAsync(Context, progress, source.Token);

        Assert.True(result.IsSuccess);
        Assert.Same(progress, runner.LastProgress);
        Assert.Equal(source.Token, runner.LastCancellationToken);
        Assert.Equal(new[] { "pub", "get" }, runner.LastRequest!.Arguments);
    }

    [Fact]
    public async Task DevicesAndEmulators_UseDistinctTypedOperations()
    {
        var runner = new RecordingProcessRunner(Success("[]"));
        var service = new FlutterCommandService(runner, new FlutterCommandBuilder());

        var devices = await service.DevicesAsync(Context);
        Assert.Equal(FlutterCommandOperation.Devices, devices.Operation);
        Assert.Equal(new[] { "devices", "--machine" }, runner.LastRequest!.Arguments);

        var emulators = await service.EmulatorsAsync(Context);
        Assert.Equal(FlutterCommandOperation.Emulators, emulators.Operation);
        Assert.Equal(new[] { "emulators" }, runner.LastRequest!.Arguments);
    }

    [Fact]
    public async Task RunAsync_PreservesFlavorTargetAndDeviceAsTypedArguments()
    {
        var runner = new RecordingProcessRunner(Success("Launching..."));
        var service = new FlutterCommandService(runner, new FlutterCommandBuilder());

        var execution = await service.RunAsync(new FlutterRunRequest(
            Context,
            "pixel_api_36",
            "qa",
            "lib/main_qa.dart"));

        Assert.True(execution.IsSuccess);
        Assert.Equal(
            new[] { "run", "-d", "pixel_api_36", "--flavor", "qa", "-t", "lib/main_qa.dart" },
            runner.LastRequest!.Arguments);
    }

    private static ProcessResult Success(params string[] lines)
        => Result(ProcessExecutionStatus.Succeeded, 0, null, lines);

    private static ProcessResult Result(
        ProcessExecutionStatus status,
        int? exitCode,
        string? failureReason,
        params string[] lines)
    {
        var now = DateTimeOffset.UtcNow;
        return new ProcessResult(
            status,
            exitCode,
            now,
            now.AddMilliseconds(20),
            lines.Select(line => new ProcessOutputLine(now, ProcessStream.StdOut, line)).ToArray(),
            "flutter command",
            failureReason);
    }

    private sealed class RecordingProcessRunner : IProcessRunner
    {
        private readonly ProcessResult _result;

        public RecordingProcessRunner(ProcessResult result)
        {
            _result = result;
        }

        public ProcessRequest? LastRequest { get; private set; }
        public IProgress<ProcessOutputLine>? LastProgress { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            LastProgress = progress;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(_result);
        }
    }
}
