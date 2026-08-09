using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Flutter.Detection;
using FlutterBuildDoctor.Flutter.Doctor;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class FlutterDoctorExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_BatchFlutter_RunsDoctorVerboseThroughCmdAndPreservesProcessResult()
    {
        var output = new[]
        {
            Line(ProcessStream.StdOut, "[✓] Flutter"),
            Line(ProcessStream.StdErr, "diagnostic stderr")
        };
        var processResult = Result(ProcessExecutionStatus.Succeeded, 0, output);
        var runner = new RecordingRunner(processResult);
        var executor = new FlutterDoctorExecutor(runner);
        var streamed = new List<ProcessOutputLine>();

        var result = await executor.ExecuteAsync(
            new FlutterDoctorExecutionRequest(Flutter(@"C:\Program Files\flutter\bin\flutter.bat")),
            new InlineProgress<ProcessOutputLine>(streamed.Add));

        Assert.Equal(FlutterDoctorExecutionStatus.Succeeded, result.Status);
        Assert.Same(processResult, result.ProcessResult);
        var request = Assert.Single(runner.Requests);
        Assert.Equal("cmd.exe", request.FileName);
        Assert.Equal(new[] { "/d", "/v:off", "/c", "call \"C:\\Program Files\\flutter\\bin\\flutter.bat\" doctor -v" }, request.Arguments);
        Assert.Equal(TimeSpan.FromMinutes(5), request.Timeout);
        Assert.Equal("Flutter doctor -v", request.DisplayName);
        Assert.Equal(output, result.ProcessResult!.Output);
        Assert.Equal(output, streamed);
    }

    [Fact]
    public async Task ExecuteAsync_ExecutableFlutter_RunsDirectDoctorVerbose()
    {
        var runner = new RecordingRunner(Result(ProcessExecutionStatus.Succeeded, 0));
        var executor = new FlutterDoctorExecutor(runner);

        var result = await executor.ExecuteAsync(
            new FlutterDoctorExecutionRequest(Flutter(@"C:\tools\flutter.exe"), TimeSpan.FromSeconds(30)));

        Assert.True(result.IsSuccess);
        var request = Assert.Single(runner.Requests);
        Assert.Equal(@"C:\tools\flutter.exe", request.FileName);
        Assert.Equal(new[] { "doctor", "-v" }, request.Arguments);
        Assert.Equal(TimeSpan.FromSeconds(30), request.Timeout);
    }

    [Fact]
    public async Task ExecuteAsync_MissingFlutter_DoesNotStartProcess()
    {
        var runner = new RecordingRunner(Result(ProcessExecutionStatus.Succeeded, 0));
        var executor = new FlutterDoctorExecutor(runner);

        var result = await executor.ExecuteAsync(new FlutterDoctorExecutionRequest(MissingFlutter()));

        Assert.Equal(FlutterDoctorExecutionStatus.FlutterUnavailable, result.Status);
        Assert.Empty(runner.Requests);
        Assert.Null(result.ProcessResult);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidTimeout_DoesNotStartProcess()
    {
        var runner = new RecordingRunner(Result(ProcessExecutionStatus.Succeeded, 0));
        var executor = new FlutterDoctorExecutor(runner);

        var result = await executor.ExecuteAsync(
            new FlutterDoctorExecutionRequest(Flutter(@"C:\flutter\bin\flutter.bat"), TimeSpan.Zero));

        Assert.Equal(FlutterDoctorExecutionStatus.InvalidRequest, result.Status);
        Assert.Empty(runner.Requests);
    }

    [Theory]
    [InlineData(ProcessExecutionStatus.Failed, FlutterDoctorExecutionStatus.Failed)]
    [InlineData(ProcessExecutionStatus.Cancelled, FlutterDoctorExecutionStatus.Cancelled)]
    [InlineData(ProcessExecutionStatus.TimedOut, FlutterDoctorExecutionStatus.TimedOut)]
    public async Task ExecuteAsync_MapsProcessTerminalStatusAndKeepsEvidence(
        ProcessExecutionStatus processStatus,
        FlutterDoctorExecutionStatus expectedStatus)
    {
        var processResult = Result(processStatus, processStatus == ProcessExecutionStatus.Failed ? 1 : null, Line(ProcessStream.StdErr, "raw evidence"));
        var runner = new RecordingRunner(processResult);
        var executor = new FlutterDoctorExecutor(runner);

        var result = await executor.ExecuteAsync(new FlutterDoctorExecutionRequest(Flutter(@"C:\flutter\bin\flutter.bat")));

        Assert.Equal(expectedStatus, result.Status);
        Assert.Same(processResult, result.ProcessResult);
        Assert.Equal("raw evidence", Assert.Single(result.ProcessResult!.Output).Text);
    }

    [Fact]
    public async Task ExecuteAsync_PreCancelled_DoesNotStartProcess()
    {
        var runner = new RecordingRunner(Result(ProcessExecutionStatus.Succeeded, 0));
        var executor = new FlutterDoctorExecutor(runner);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await executor.ExecuteAsync(
            new FlutterDoctorExecutionRequest(Flutter(@"C:\flutter\bin\flutter.bat")),
            cancellationToken: cancellation.Token);

        Assert.Equal(FlutterDoctorExecutionStatus.Cancelled, result.Status);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task ExecuteAsync_RunnerThrowsCancellation_ReturnsCancelled()
    {
        var runner = new ThrowingRunner(new OperationCanceledException());
        var executor = new FlutterDoctorExecutor(runner);

        var result = await executor.ExecuteAsync(new FlutterDoctorExecutionRequest(Flutter(@"C:\flutter\bin\flutter.bat")));

        Assert.Equal(FlutterDoctorExecutionStatus.Cancelled, result.Status);
        Assert.Null(result.ProcessResult);
    }

    private static FlutterDetectionResult Flutter(string executablePath)
        => new(
            FlutterSdkDetectionStatus.Succeeded,
            Installed: true,
            FlutterPath: executablePath,
            FlutterSdkPath: Path.GetDirectoryName(Path.GetDirectoryName(executablePath)),
            FlutterVersion: "3.44.8",
            Channel: "stable",
            Candidates: Array.Empty<FlutterSdkCandidate>(),
            HasConflict: false,
            Message: "Flutter detected.",
            PathDiscovery: EmptyPathDiscovery());

    private static FlutterDetectionResult MissingFlutter()
        => new(
            FlutterSdkDetectionStatus.Missing,
            Installed: false,
            FlutterPath: null,
            FlutterSdkPath: null,
            FlutterVersion: null,
            Channel: null,
            Candidates: Array.Empty<FlutterSdkCandidate>(),
            HasConflict: false,
            Message: "Flutter missing.",
            PathDiscovery: EmptyPathDiscovery());

    private static PathExecutableDiscoveryResult EmptyPathDiscovery()
        => new(
            PathExecutableDiscoveryStatus.Succeeded,
            "flutter",
            Array.Empty<PathExecutableMatch>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<IgnoredPathEntry>(),
            "No matches.");

    private static ProcessOutputLine Line(ProcessStream stream, string text)
        => new(DateTimeOffset.UtcNow, stream, text);

    private static ProcessResult Result(
        ProcessExecutionStatus status,
        int? exitCode,
        params ProcessOutputLine[] output)
    {
        var now = DateTimeOffset.UtcNow;
        return new ProcessResult(status, exitCode, now, now.AddMilliseconds(10), output, "flutter doctor -v");
    }

    private sealed class RecordingRunner : IProcessRunner
    {
        private readonly ProcessResult _result;

        public RecordingRunner(ProcessResult result) => _result = result;

        public List<ProcessRequest> Requests { get; } = new();

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            foreach (var line in _result.Output)
                progress?.Report(line);
            return Task.FromResult(_result);
        }
    }

    private sealed class ThrowingRunner : IProcessRunner
    {
        private readonly Exception _exception;

        public ThrowingRunner(Exception exception) => _exception = exception;

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromException<ProcessResult>(_exception);
    }

    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;

        public InlineProgress(Action<T> report) => _report = report;

        public void Report(T value) => _report(value);
    }
}
