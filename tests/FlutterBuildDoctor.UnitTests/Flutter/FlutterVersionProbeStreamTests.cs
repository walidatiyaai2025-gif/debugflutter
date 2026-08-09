using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Flutter.Detection;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class FlutterVersionProbeStreamTests
{
    [Fact]
    public async Task ProbeAsync_VersionLikeStderr_DoesNotPopulateStructuredFields()
    {
        var output = new[]
        {
            Err("Flutter 9.9.9 • channel fake • https://example.invalid/flutter.git"),
            Err("Framework • revision fake-framework • 2099-01-01"),
            Err("Tools • Dart 99.0.0 • DevTools 99.0.0")
        };
        var process = Process(output);
        var probe = new FlutterVersionProbe(new RecordingRunner(process));

        var result = await probe.ProbeAsync(
            new FlutterVersionProbeRequest(Flutter(@"C:\flutter\bin\flutter.bat")));

        Assert.Equal(FlutterVersionProbeStatus.ParseFailed, result.Status);
        Assert.Null(result.FlutterVersion);
        Assert.Null(result.Channel);
        Assert.Null(result.RepositoryUrl);
        Assert.Null(result.FrameworkRevision);
        Assert.Null(result.DartVersion);
        Assert.False(result.HasRequiredVersionFields);
        Assert.Same(process, result.ProcessResult);
        Assert.Equal(output, result.ProcessResult!.Output);
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
            PathDiscovery: new PathExecutableDiscoveryResult(
                PathExecutableDiscoveryStatus.Succeeded,
                "flutter",
                Array.Empty<PathExecutableMatch>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<IgnoredPathEntry>(),
                "No matches."));

    private static ProcessOutputLine Err(string text)
        => new(DateTimeOffset.UtcNow, ProcessStream.StdErr, text);

    private static ProcessResult Process(params ProcessOutputLine[] output)
    {
        var now = DateTimeOffset.UtcNow;
        return new ProcessResult(
            ProcessExecutionStatus.Succeeded,
            0,
            now,
            now.AddMilliseconds(10),
            output,
            "flutter --version");
    }

    private sealed class RecordingRunner : IProcessRunner
    {
        private readonly ProcessResult _result;

        public RecordingRunner(ProcessResult result) => _result = result;

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
    }
}
