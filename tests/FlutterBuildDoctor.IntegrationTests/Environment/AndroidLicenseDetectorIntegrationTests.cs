using FlutterBuildDoctor.Android.Detection;
using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.IntegrationTests.Environment;

public sealed class AndroidLicenseDetectorIntegrationTests
{
    [Fact]
    public async Task DetectAsync_ComposesFromSdkRootAndCommandLineTools_WithForcedClosedInput()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlutterBuildDoctorTests", "LicenseIntegration", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "platform-tools"));
            var tools = Path.Combine(root, "cmdline-tools", "latest");
            var bin = Path.Combine(tools, "bin");
            Directory.CreateDirectory(bin);
            File.WriteAllText(Path.Combine(tools, "source.properties"), "Pkg.Revision=19.0\n");
            var sdkManagerPath = Path.Combine(bin, "sdkmanager.bat");
            File.WriteAllText(sdkManagerPath, "@echo off");

            var licenses = Path.Combine(root, "licenses");
            Directory.CreateDirectory(licenses);
            File.WriteAllText(Path.Combine(licenses, "android-sdk-license"), "hash");

            var sdkRootResult = new AndroidSdkRootDetector().Detect(Snapshot(root));
            var commandLineResult = new AndroidCommandLineToolsDetector().Detect(sdkRootResult);
            var runner = new StubProcessRunner(SuccessResult());
            var result = await new AndroidLicenseDetector(runner).DetectAsync(commandLineResult);

            Assert.True(sdkRootResult.IsSuccess, sdkRootResult.Message);
            Assert.True(commandLineResult.IsSuccess, commandLineResult.Message);
            Assert.True(result.IsReady, result.Message);
            Assert.Equal(new[] { "android-sdk-license" }, result.LicenseFiles);

            var request = Assert.Single(runner.Requests);
            Assert.Equal("cmd.exe", request.FileName, ignoreCase: true);
            Assert.Equal(5, request.Arguments.Count);
            Assert.Equal("/c", request.Arguments[3], ignoreCase: true);
            var command = request.Arguments[4];
            Assert.Contains($"call \"{sdkManagerPath}\" --licenses", command, StringComparison.Ordinal);
            Assert.Contains("< NUL", command, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("echo y", command, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("yes |", command, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static EnvironmentVariableSnapshot Snapshot(string androidSdkRoot)
        => new(DateTimeOffset.UtcNow, MissingRecord("PATH"), MissingRecord("JAVA_HOME"), MissingRecord("ANDROID_HOME"),
            new VariableRecord("ANDROID_SDK_ROOT",
                new VariableScopeValue(VariableScope.Process, VariableReadStatus.Present, androidSdkRoot),
                new VariableScopeValue(VariableScope.User, VariableReadStatus.Missing, null),
                new VariableScopeValue(VariableScope.Machine, VariableReadStatus.Missing, null)));

    private static VariableRecord MissingRecord(string name)
        => new(name,
            new VariableScopeValue(VariableScope.Process, VariableReadStatus.Missing, null),
            new VariableScopeValue(VariableScope.User, VariableReadStatus.Missing, null),
            new VariableScopeValue(VariableScope.Machine, VariableReadStatus.Missing, null));

    private static ProcessResult SuccessResult()
    {
        var now = DateTimeOffset.UtcNow;
        return new ProcessResult(ProcessExecutionStatus.Succeeded, 0, now, now,
            new[] { new ProcessOutputLine(now, ProcessStream.StdOut, "All SDK package licenses accepted.") },
            "sdkmanager --licenses");
    }

    private sealed class StubProcessRunner : IProcessRunner
    {
        private readonly ProcessResult _result;
        public StubProcessRunner(ProcessResult result) => _result = result;
        public List<ProcessRequest> Requests { get; } = new();
        public Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<ProcessOutputLine>? progress = null, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(_result);
        }
    }
}
