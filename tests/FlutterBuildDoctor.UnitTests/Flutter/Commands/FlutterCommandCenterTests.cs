using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Flutter.Commands;

namespace FlutterBuildDoctor.UnitTests.Flutter.Commands;

public sealed class FlutterCommandCenterTests
{
    [Theory]
    [InlineData(FlutterCommandKind.PubGet, "pub", "get")]
    [InlineData(FlutterCommandKind.Clean, "clean", null)]
    [InlineData(FlutterCommandKind.Analyze, "analyze", null)]
    [InlineData(FlutterCommandKind.Test, "test", null)]
    [InlineData(FlutterCommandKind.PubOutdated, "pub", "outdated")]
    [InlineData(FlutterCommandKind.Devices, "devices", null)]
    [InlineData(FlutterCommandKind.Emulators, "emulators", null)]
    public void Builder_ProducesTypedArgumentsWithoutShellConcatenation(FlutterCommandKind kind, string first, string? second)
    {
        var builder = new FlutterCommandBuilder();
        var request = builder.Build(new FlutterCommandRequest(kind, @"C:\flutter\bin\flutter.bat", @"C:\repo"));

        Assert.Equal(@"C:\flutter\bin\flutter.bat", request.FileName);
        Assert.Equal(first, request.Arguments[0]);
        if (second is not null) Assert.Equal(second, request.Arguments[1]);
        Assert.Equal(@"C:\repo", request.WorkingDirectory);
        Assert.DoesNotContain("cmd", request.FileName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Builder_Run_PreservesDeviceFlavorTargetAndDartDefinesAsSeparateArguments()
    {
        var builder = new FlutterCommandBuilder();
        var process = builder.Build(new FlutterCommandRequest(
            FlutterCommandKind.Run,
            "flutter",
            @"C:\repo",
            new FlutterRunRequest(
                DeviceId: "emulator-5554",
                Flavor: "staging",
                Target: "lib/main_staging.dart",
                Debug: true,
                DartDefines: new[] { "API_URL=https://example.test", "FEATURE_X=true" })));

        Assert.Equal(new[]
        {
            "run", "-d", "emulator-5554", "--flavor", "staging", "--target", "lib/main_staging.dart",
            "--debug", "--dart-define", "API_URL=https://example.test", "--dart-define", "FEATURE_X=true"
        }, process.Arguments);
    }

    [Fact]
    public async Task ExecuteAsync_ForwardsProgressAndCancellationTokenToProcessRunner()
    {
        using var cts = new CancellationTokenSource();
        var runner = new CapturingRunner(CreateSuccess("Resolving dependencies..."));
        var center = new FlutterCommandCenter(runner, new FlutterCommandBuilder());
        var progress = new InlineProgress();

        var result = await center.PubGetAsync("flutter", @"C:\repo", progress, cts.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(cts.Token, runner.CancellationToken);
        Assert.Same(progress, runner.Progress);
        Assert.Equal(new[] { "pub", "get" }, runner.Request!.Arguments);
    }

    [Fact]
    public async Task AnalyzeAsync_ProjectsIssueSummary()
    {
        var runner = new CapturingRunner(CreateSuccess(
            "error - lib/a.dart: missing symbol",
            "warning - lib/b.dart: deprecated API",
            "info - lib/c.dart: style"));
        var center = new FlutterCommandCenter(runner, new FlutterCommandBuilder());

        var result = await center.AnalyzeAsync("flutter", @"C:\repo");

        Assert.NotNull(result.Analyze);
        Assert.Equal(3, result.Analyze!.IssueCount);
        Assert.Equal(1, result.Analyze.ErrorCount);
        Assert.Equal(1, result.Analyze.WarningCount);
        Assert.Equal(1, result.Analyze.InfoCount);
    }

    [Fact]
    public async Task TestAsync_PreservesCancelledAndTimedOutStates()
    {
        var cancelled = new FlutterCommandCenter(new CapturingRunner(CreateResult(ProcessExecutionStatus.Cancelled)), new FlutterCommandBuilder());
        var timedOut = new FlutterCommandCenter(new CapturingRunner(CreateResult(ProcessExecutionStatus.TimedOut)), new FlutterCommandBuilder());

        var cancelledResult = await cancelled.TestAsync("flutter", @"C:\repo");
        var timedOutResult = await timedOut.TestAsync("flutter", @"C:\repo");

        Assert.True(cancelledResult.IsCancelled);
        Assert.True(timedOutResult.IsTimedOut);
        Assert.False(cancelledResult.IsSuccess);
        Assert.False(timedOutResult.IsSuccess);
    }

    [Fact]
    public async Task TestAsync_ProjectsBasicPassFailSkipEvidence()
    {
        var runner = new CapturingRunner(CreateSuccess("00:01 +8: All tests passed!", "1 skipped"));
        var center = new FlutterCommandCenter(runner, new FlutterCommandBuilder());

        var result = await center.TestAsync("flutter", @"C:\repo");

        Assert.NotNull(result.Tests);
        Assert.Equal(1, result.Tests!.Passed);
        Assert.Equal(0, result.Tests.Failed);
        Assert.Equal(1, result.Tests.Skipped);
    }

    private static ProcessResult CreateSuccess(params string[] lines)
        => CreateResult(ProcessExecutionStatus.Succeeded, lines);

    private static ProcessResult CreateResult(ProcessExecutionStatus status, params string[] lines)
    {
        var now = DateTimeOffset.UtcNow;
        return new ProcessResult(
            status,
            status == ProcessExecutionStatus.Succeeded ? 0 : null,
            now,
            now.AddMilliseconds(25),
            lines.Select(text => new ProcessOutputLine(now, ProcessStream.StdOut, text)).ToArray(),
            "flutter test");
    }

    private sealed class CapturingRunner : IProcessRunner
    {
        private readonly ProcessResult _result;
        public CapturingRunner(ProcessResult result) => _result = result;
        public ProcessRequest? Request { get; private set; }
        public IProgress<ProcessOutputLine>? Progress { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<ProcessOutputLine>? progress = null, CancellationToken cancellationToken = default)
        {
            Request = request;
            Progress = progress;
            CancellationToken = cancellationToken;
            return Task.FromResult(_result);
        }
    }

    private sealed class InlineProgress : IProgress<ProcessOutputLine>
    {
        public void Report(ProcessOutputLine value) { }
    }
}
