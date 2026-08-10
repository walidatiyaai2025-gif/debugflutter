using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Flutter.Detection;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class FlutterVersionProbeTests
{
    [Fact]
    public async Task ProbeAsync_UsesTypedVersionArgumentAndParsesStructuredVersion()
    {
        var runner = new StubProcessRunner(Success(
            "Flutter 3.35.0 • channel stable • https://github.com/flutter/flutter.git",
            "Framework • revision abc1234 (2 days ago) • 2026-08-08 10:00:00 +0000",
            "Engine • hash 111aaa • revision def5678",
            "Tools • Dart 3.9.0 • DevTools 2.48.0"));
        var probe = new FlutterVersionProbe(runner);

        var result = await probe.ProbeAsync(@"C:\flutter\bin\flutter.bat", @"C:\work\app");

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "--version" }, runner.LastRequest!.Arguments);
        Assert.Equal(@"C:\work\app", runner.LastRequest.WorkingDirectory);
        Assert.Equal("3.35.0", result.Version!.FlutterVersion);
        Assert.Equal("stable", result.Version.Channel);
        Assert.Equal("abc1234", result.Version.FrameworkRevision);
        Assert.Equal("def5678", result.Version.EngineRevision);
        Assert.Equal("3.9.0", result.Version.DartVersion);
        Assert.Equal("2.48.0", result.Version.DevToolsVersion);
    }

    [Fact]
    public async Task ProbeAsync_PreservesRawOutputWhenParsingFails()
    {
        var runner = new StubProcessRunner(Success("unexpected future output"));
        var probe = new FlutterVersionProbe(runner);

        var result = await probe.ProbeAsync("flutter");

        Assert.Equal(FlutterVersionProbeStatus.ParseFailed, result.Status);
        Assert.Null(result.Version);
        Assert.Contains("unexpected future output", result.RawOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeAsync_MapsTimeoutWithoutPretendingVersionWasParsed()
    {
        var now = DateTimeOffset.UtcNow;
        var processResult = new ProcessResult(
            ProcessExecutionStatus.TimedOut,
            null,
            now,
            now.AddSeconds(30),
            new[] { new ProcessOutputLine(now, ProcessStream.StdErr, "timed out") },
            "flutter --version",
            "Timed out.");
        var probe = new FlutterVersionProbe(new StubProcessRunner(processResult));

        var result = await probe.ProbeAsync("flutter");

        Assert.Equal(FlutterVersionProbeStatus.TimedOut, result.Status);
        Assert.Null(result.Version);
        Assert.Equal("timed out", result.RawOutput);
    }

    private static ProcessResult Success(params string[] lines)
    {
        var now = DateTimeOffset.UtcNow;
        return new ProcessResult(
            ProcessExecutionStatus.Succeeded,
            0,
            now,
            now.AddMilliseconds(25),
            lines.Select(line => new ProcessOutputLine(now, ProcessStream.StdOut, line)).ToArray(),
            "flutter --version");
    }

    private sealed class StubProcessRunner : IProcessRunner
    {
        private readonly ProcessResult _result;

        public StubProcessRunner(ProcessResult result)
        {
            _result = result;
        }

        public ProcessRequest? LastRequest { get; private set; }

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(_result);
        }
    }
}
