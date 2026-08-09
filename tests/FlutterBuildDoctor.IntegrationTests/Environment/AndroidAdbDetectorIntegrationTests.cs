using FlutterBuildDoctor.Android.Detection;
using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.IntegrationTests.Environment;

public sealed class AndroidAdbDetectorIntegrationTests
{
    [Fact]
    public async Task DetectAsync_ComposesWithAndroidSdkRootDetector_AndUsesOnlyVersionProbe()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlutterBuildDoctorTests", "AdbIntegration", Guid.NewGuid().ToString("N"));
        try
        {
            var platformTools = Path.Combine(root, "platform-tools");
            Directory.CreateDirectory(platformTools);
            var adbPath = Path.Combine(platformTools, "adb.exe");
            File.WriteAllText(adbPath, "fixture");
            File.WriteAllText(Path.Combine(platformTools, "source.properties"), "Pkg.Revision=36.0.0\n");

            var sdkRootResult = new AndroidSdkRootDetector().Detect(Snapshot(root));
            var runner = new StubProcessRunner(SuccessResult(adbPath));
            var result = await new AndroidAdbDetector(runner).DetectAsync(sdkRootResult);

            Assert.True(sdkRootResult.IsSuccess, sdkRootResult.Message);
            Assert.True(result.IsSuccess, result.Message);
            Assert.Equal("1.0.41", result.AdbProtocolVersion);
            Assert.Equal("36.0.0-13206524", result.PlatformToolsVersion);
            var request = Assert.Single(runner.Requests);
            Assert.Equal(adbPath, request.FileName, ignoreCase: true);
            Assert.Equal(new[] { "version" }, request.Arguments);
            Assert.DoesNotContain(request.Arguments, argument =>
                argument.Contains("server", StringComparison.OrdinalIgnoreCase) ||
                argument.Contains("devices", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }

    private static EnvironmentVariableSnapshot Snapshot(string androidSdkRoot)
        => new(
            DateTimeOffset.UtcNow,
            MissingRecord("PATH"),
            MissingRecord("JAVA_HOME"),
            MissingRecord("ANDROID_HOME"),
            new VariableRecord(
                "ANDROID_SDK_ROOT",
                new VariableScopeValue(VariableScope.Process, VariableReadStatus.Present, androidSdkRoot),
                new VariableScopeValue(VariableScope.User, VariableReadStatus.Missing, null),
                new VariableScopeValue(VariableScope.Machine, VariableReadStatus.Missing, null)));

    private static VariableRecord MissingRecord(string name)
        => new(
            name,
            new VariableScopeValue(VariableScope.Process, VariableReadStatus.Missing, null),
            new VariableScopeValue(VariableScope.User, VariableReadStatus.Missing, null),
            new VariableScopeValue(VariableScope.Machine, VariableReadStatus.Missing, null));

    private static ProcessResult SuccessResult(string adbPath)
    {
        var now = DateTimeOffset.UtcNow;
        return new ProcessResult(
            ProcessExecutionStatus.Succeeded,
            0,
            now,
            now,
            new[]
            {
                new ProcessOutputLine(now, ProcessStream.StdOut, "Android Debug Bridge version 1.0.41"),
                new ProcessOutputLine(now, ProcessStream.StdOut, "Version 36.0.0-13206524"),
                new ProcessOutputLine(now, ProcessStream.StdOut, $"Installed as {adbPath}")
            },
            "adb version");
    }

    private sealed class StubProcessRunner : IProcessRunner
    {
        private readonly ProcessResult _result;

        public StubProcessRunner(ProcessResult result)
        {
            _result = result;
        }

        public List<ProcessRequest> Requests { get; } = new();

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(_result);
        }
    }
}
