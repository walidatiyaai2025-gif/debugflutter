using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Flutter.Detection;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class FlutterVersionProbeRobustnessTests
{
    [Fact]
    public async Task ProbeAsync_StderrCannotSupplyStructuredVersionFields()
    {
        var output = new[]
        {
            Err("Flutter 9.9.9 • channel fake • https://example.invalid/flutter.git"),
            Err("Framework • revision fake-framework"),
            Err("Tools • Dart 9.9.9 • DevTools 9.9.9"),
            Out("Flutter 3.44.8 • channel stable • https://github.com/flutter/flutter.git"),
            Out("Framework • revision real-framework (today) • 2026-08-09"),
            Out("Tools • Dart 3.12.2 • DevTools 2.57.0")
        };
        var process = Process(ProcessExecutionStatus.Succeeded, 0, output);
        var probe = new FlutterVersionProbe(new ResultRunner(process));

        var result = await probe.ProbeAsync(
            new FlutterVersionProbeRequest(Flutter(@"C:\flutter\bin\flutter.bat")));

        Assert.Equal(FlutterVersionProbeStatus.Succeeded, result.Status);
        Assert.Equal("3.44.8", result.FlutterVersion);
        Assert.Equal("stable", result.Channel);
        Assert.Equal("real-framework", result.FrameworkRevision);
        Assert.Equal("3.12.2", result.DartVersion);
        Assert.Same(process, result.ProcessResult);
        Assert.Equal(output, result.ProcessResult!.Output);
    }

    [Fact]
    public async Task ProbeAsync_WindowsOemBellSeparator_ParsesWithoutChangingRawEvidence()
    {
        var output = new[]
        {
            Out("Flutter 3.44.8 \a channel stable \a https://github.com/flutter/flutter.git"),
            Out("Framework \a revision abc123def (today) \a 2026-08-09"),
            Out("Engine \a revision engine987"),
            Out("Tools \a Dart 3.12.2 \a DevTools 2.57.0")
        };
        var process = Process(ProcessExecutionStatus.Succeeded, 0, output);
        var probe = new FlutterVersionProbe(new ResultRunner(process));

        var result = await probe.ProbeAsync(
            new FlutterVersionProbeRequest(Flutter(@"C:\flutter\bin\flutter.bat")));

        Assert.Equal(FlutterVersionProbeStatus.Succeeded, result.Status);
        Assert.Equal("3.44.8", result.FlutterVersion);
        Assert.Equal("stable", result.Channel);
        Assert.Equal("abc123def", result.FrameworkRevision);
        Assert.Equal("engine987", result.EngineRevision);
        Assert.Equal("3.12.2", result.DartVersion);
        Assert.Equal("2.57.0", result.DevToolsVersion);
        Assert.Same(process, result.ProcessResult);
        Assert.Equal(output, result.ProcessResult!.Output);
        Assert.Contains('\a', result.ProcessResult.Output[0].Text);
    }

    [Theory]
    [InlineData("ΓÇó")]
    [InlineData("â€¢")]
    public async Task ProbeAsync_WindowsMojibakeBulletSeparator_ParsesWithoutChangingRawEvidence(string separator)
    {
        var output = new[]
        {
            Out($"Flutter 3.44.8 {separator} channel stable {separator} https://github.com/flutter/flutter.git"),
            Out($"Framework {separator} revision abc123def (today) {separator} 2026-08-09"),
            Out($"Engine {separator} revision engine987"),
            Out($"Tools {separator} Dart 3.12.2 {separator} DevTools 2.57.0")
        };
        var process = Process(ProcessExecutionStatus.Succeeded, 0, output);
        var probe = new FlutterVersionProbe(new ResultRunner(process));

        var result = await probe.ProbeAsync(
            new FlutterVersionProbeRequest(Flutter(@"C:\flutter\bin\flutter.bat")));

        Assert.Equal(FlutterVersionProbeStatus.Succeeded, result.Status);
        Assert.Equal("3.44.8", result.FlutterVersion);
        Assert.Equal("stable", result.Channel);
        Assert.Equal("abc123def", result.FrameworkRevision);
        Assert.Equal("engine987", result.EngineRevision);
        Assert.Equal("3.12.2", result.DartVersion);
        Assert.Equal("2.57.0", result.DevToolsVersion);
        Assert.Same(process, result.ProcessResult);
        Assert.Equal(output, result.ProcessResult!.Output);
        Assert.Contains(separator, result.ProcessResult.Output[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeAsync_RunnerFailure_ReturnsProbeFailedWithoutInventingEvidence()
    {
        var probe = new FlutterVersionProbe(new ThrowingRunner(new InvalidOperationException("start failed")));

        var result = await probe.ProbeAsync(
            new FlutterVersionProbeRequest(Flutter(@"C:\flutter\bin\flutter.bat")));

        Assert.Equal(FlutterVersionProbeStatus.ProbeFailed, result.Status);
        Assert.Null(result.ProcessResult);
        Assert.Null(result.FlutterVersion);
        Assert.Contains("start failed", result.Message, StringComparison.Ordinal);
    }

    private static FlutterDetectionResult Flutter(string executablePath)
        => new(
            FlutterSdkDetectionStatus.Succeeded,
            Installed: true,
            FlutterPath: executablePath,
            FlutterSdkPath: @"C:\flutter",
            FlutterVersion: "metadata-version",
            Channel: "metadata-channel",
            Candidates: Array.Empty<FlutterSdkCandidate>(),
            HasConflict: false,
            Message: "Flutter detected.",
            PathDiscovery: new PathExecutableDiscoveryResult(
                PathExecutableDiscoveryStatus.Succeeded,
                "flutter",
                Array.Empty<PathExecutableMatch>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<IgnoredPathEntry>(),
                "No matches."));

    private static ProcessResult Process(
        ProcessExecutionStatus status,
        int? exitCode,
        params ProcessOutputLine[] output)
    {
        var now = DateTimeOffset.UtcNow;
        return new ProcessResult(
            status,
            exitCode,
            now,
            now.AddMilliseconds(10),
            output,
            "flutter --version");
    }

    private static ProcessOutputLine Out(string text)
        => new(DateTimeOffset.UtcNow, ProcessStream.StdOut, text);

    private static ProcessOutputLine Err(string text)
        => new(DateTimeOffset.UtcNow, ProcessStream.StdErr, text);

    private sealed class ResultRunner : IProcessRunner
    {
        private readonly ProcessResult _result;

        public ResultRunner(ProcessResult result) => _result = result;

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
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
}
