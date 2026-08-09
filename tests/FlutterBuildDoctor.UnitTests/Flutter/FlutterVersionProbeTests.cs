using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Flutter.Detection;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class FlutterVersionProbeTests
{
    [Fact]
    public async Task ProbeAsync_BatchFlutter_ParsesModernOutputAndPreservesProcessEvidence()
    {
        var output = new[]
        {
            Out("Flutter 3.44.8 • channel stable • https://github.com/flutter/flutter.git"),
            Out("Framework • revision abc123def (5 days ago) • 2026-08-04 12:00:00 +0000"),
            Out("Engine • hash engine987 (revision old-engine) • 2026-08-04 12:00:00.000Z"),
            Out("Tools • Dart 3.12.2 • DevTools 2.57.0"),
            Err("diagnostic stderr preserved")
        };
        var process = Process(ProcessExecutionStatus.Succeeded, 0, output);
        var runner = new RecordingRunner(process);
        var probe = new FlutterVersionProbe(runner);
        var streamed = new List<ProcessOutputLine>();

        var result = await probe.ProbeAsync(
            new FlutterVersionProbeRequest(Flutter(@"C:\Program Files\flutter\bin\flutter.bat")),
            new InlineProgress<ProcessOutputLine>(streamed.Add));

        Assert.Equal(FlutterVersionProbeStatus.Succeeded, result.Status);
        Assert.Equal("3.44.8", result.FlutterVersion);
        Assert.Equal("stable", result.Channel);
        Assert.Equal("abc123def", result.FrameworkRevision);
        Assert.Equal("3.12.2", result.DartVersion);
        Assert.Equal("engine987", result.EngineRevision);
        Assert.Equal("2.57.0", result.DevToolsVersion);
        Assert.Same(process, result.ProcessResult);
        Assert.Equal(output, result.ProcessResult!.Output);
        Assert.Equal(output, streamed);

        var request = Assert.Single(runner.Requests);
        Assert.Equal("cmd.exe", request.FileName);
        Assert.Equal(new[] { "/d", "/v:off", "/c", @"C:\Program Files\flutter\bin\flutter.bat", "--version" }, request.Arguments);
        Assert.Equal(TimeSpan.FromMinutes(1), request.Timeout);
        Assert.Equal("Flutter --version", request.DisplayName);
    }

    [Fact]
    public async Task ProbeAsync_ExecutableFlutter_ParsesEngineRevisionAndUsesRequestedTimeout()
    {
        var runner = new RecordingRunner(Process(
            ProcessExecutionStatus.Succeeded,
            0,
            Out("Flutter 3.24.0 • channel beta • https://github.com/flutter/flutter.git"),
            Out("Framework • revision fff111 (2 weeks ago) • 2026-07-20"),
            Out("Engine • revision eee222"),
            Out("Tools • Dart 3.5.0 • DevTools 2.37.2")));
        var probe = new FlutterVersionProbe(runner);

        var result = await probe.ProbeAsync(
            new FlutterVersionProbeRequest(Flutter(@"C:\tools\flutter.exe"), TimeSpan.FromSeconds(20)));

        Assert.True(result.IsSuccess);
        Assert.Equal("beta", result.Channel);
        Assert.Equal("eee222", result.EngineRevision);
        var request = Assert.Single(runner.Requests);
        Assert.Equal(@"C:\tools\flutter.exe", request.FileName);
        Assert.Equal(new[] { "--version" }, request.Arguments);
        Assert.Equal(TimeSpan.FromSeconds(20), request.Timeout);
    }

    [Fact]
    public async Task ProbeAsync_PartialOutput_ReturnsParseFailedWithPartialFieldsAndRawEvidence()
    {
        var process = Process(
            ProcessExecutionStatus.Succeeded,
            0,
            Out("Flutter 3.44.8 • channel stable • https://github.com/flutter/flutter.git"),
            Out("Tools • Dart 3.12.2"));
        var probe = new FlutterVersionProbe(new RecordingRunner(process));

        var result = await probe.ProbeAsync(new FlutterVersionProbeRequest(Flutter(@"C:\flutter\bin\flutter.bat")));

        Assert.Equal(FlutterVersionProbeStatus.ParseFailed, result.Status);
        Assert.Equal("3.44.8", result.FlutterVersion);
        Assert.Equal("stable", result.Channel);
        Assert.Equal("3.12.2", result.DartVersion);
        Assert.Null(result.FrameworkRevision);
        Assert.Contains("framework revision", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Same(process, result.ProcessResult);
    }

    [Fact]
    public async Task ProbeAsync_MissingFlutter_DoesNotStartProcess()
    {
        var runner = new RecordingRunner(Process(ProcessExecutionStatus.Succeeded, 0));
        var probe = new FlutterVersionProbe(runner);

        var result = await probe.ProbeAsync(new FlutterVersionProbeRequest(MissingFlutter()));

        Assert.Equal(FlutterVersionProbeStatus.FlutterUnavailable, result.Status);
        Assert.Empty(runner.Requests);
        Assert.Null(result.ProcessResult);
    }

    [Fact]
    public async Task ProbeAsync_InvalidTimeout_DoesNotStartProcess()
    {
        var runner = new RecordingRunner(Process(ProcessExecutionStatus.Succeeded, 0));
        var probe = new FlutterVersionProbe(runner);

        var result = await probe.ProbeAsync(
            new FlutterVersionProbeRequest(Flutter(@"C:\flutter\bin\flutter.bat"), TimeSpan.Zero));

        Assert.Equal(FlutterVersionProbeStatus.InvalidRequest, result.Status);
        Assert.Empty(runner.Requests);
    }

    [Theory]
    [InlineData(ProcessExecutionStatus.Failed, FlutterVersionProbeStatus.ProbeFailed)]
    [InlineData(ProcessExecutionStatus.Cancelled, FlutterVersionProbeStatus.Cancelled)]
    [InlineData(ProcessExecutionStatus.TimedOut, FlutterVersionProbeStatus.TimedOut)]
    public async Task ProbeAsync_MapsTerminalProcessStatusAndKeepsEvidence(
        ProcessExecutionStatus processStatus,
        FlutterVersionProbeStatus expectedStatus)
    {
        var process = Process(
            processStatus,
            processStatus == ProcessExecutionStatus.Failed ? 1 : null,
            Err("raw version probe evidence"));
        var probe = new FlutterVersionProbe(new RecordingRunner(process));

        var result = await probe.ProbeAsync(new FlutterVersionProbeRequest(Flutter(@"C:\flutter\bin\flutter.bat")));

        Assert.Equal(expectedStatus, result.Status);
        Assert.Same(process, result.ProcessResult);
        Assert.Equal("raw version probe evidence", Assert.Single(result.ProcessResult!.Output).Text);
    }

    [Fact]
    public async Task ProbeAsync_PreCancelled_DoesNotStartProcess()
    {
        var runner = new RecordingRunner(Process(ProcessExecutionStatus.Succeeded, 0));
        var probe = new FlutterVersionProbe(runner);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await probe.ProbeAsync(
            new FlutterVersionProbeRequest(Flutter(@"C:\flutter\bin\flutter.bat")),
            cancellationToken: cancellation.Token);

        Assert.Equal(FlutterVersionProbeStatus.Cancelled, result.Status);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task ProbeAsync_RunnerThrowsCancellation_ReturnsCancelled()
    {
        var probe = new FlutterVersionProbe(new ThrowingRunner(new OperationCanceledException()));

        var result = await probe.ProbeAsync(new FlutterVersionProbeRequest(Flutter(@"C:\flutter\bin\flutter.bat")));

        Assert.Equal(FlutterVersionProbeStatus.Cancelled, result.Status);
        Assert.Null(result.ProcessResult);
    }

    private static FlutterDetectionResult Flutter(string executablePath)
        => new(
            FlutterSdkDetectionStatus.Succeeded,
            Installed: true,
            FlutterPath: executablePath,
            FlutterSdkPath: Path.GetDirectoryName(Path.GetDirectoryName(executablePath)),
            FlutterVersion: "metadata-version",
            Channel: "metadata-channel",
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

    private static ProcessOutputLine Out(string text)
        => new(DateTimeOffset.UtcNow, ProcessStream.StdOut, text);

    private static ProcessOutputLine Err(string text)
        => new(DateTimeOffset.UtcNow, ProcessStream.StdErr, text);

    private static ProcessResult Process(
        ProcessExecutionStatus status,
        int? exitCode,
        params ProcessOutputLine[] output)
    {
        var now = DateTimeOffset.UtcNow;
        return new ProcessResult(status, exitCode, now, now.AddMilliseconds(10), output, "flutter --version");
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
