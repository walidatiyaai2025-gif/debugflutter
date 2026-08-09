using FlutterBuildDoctor.Android.Detection;
using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.IntegrationTests.Environment;

public sealed class AndroidEmulatorDetectorIntegrationTests
{
    [Fact]
    public async Task DetectAsync_ComposesFromSdkRootAndBuildsVersionOnlyProbe()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlutterBuildDoctorTests", "EmulatorIntegration", Guid.NewGuid().ToString("N"));
        try
        {
            var emulatorDirectory = Path.Combine(root, "emulator");
            Directory.CreateDirectory(emulatorDirectory);
            var emulatorPath = Path.Combine(emulatorDirectory, "emulator.exe");
            File.WriteAllText(emulatorPath, "fixture");
            File.WriteAllText(Path.Combine(emulatorDirectory, "source.properties"), "Pkg.Revision=36.1.9.0\n");

            var sdkRootResult = new AndroidSdkRootDetector().Detect(Snapshot(root));
            var runner = new StubProcessRunner(SuccessResult());
            var result = await new AndroidEmulatorDetector(runner).DetectAsync(sdkRootResult);

            Assert.True(sdkRootResult.IsSuccess, sdkRootResult.Message);
            Assert.True(result.IsSuccess, result.Message);
            Assert.Equal("36.1.9.0", result.Version);
            var request = Assert.Single(runner.Requests);
            Assert.Equal(emulatorPath, request.FileName, ignoreCase: true);
            Assert.Equal(new[] { "-version" }, request.Arguments);
            Assert.DoesNotContain(request.Arguments, argument =>
                argument.Contains("avd", StringComparison.OrdinalIgnoreCase) ||
                argument.Contains("no-window", StringComparison.OrdinalIgnoreCase));
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

    private static ProcessResult SuccessResult()
    {
        var now = DateTimeOffset.UtcNow;
        return new ProcessResult(
            ProcessExecutionStatus.Succeeded,
            0,
            now,
            now,
            new[]
            {
                new ProcessOutputLine(now, ProcessStream.StdOut, "Android emulator version 36.1.9.0 (build_id 14000000)")
            },
            "emulator -version");
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
