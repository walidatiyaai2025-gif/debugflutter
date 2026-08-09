using FlutterBuildDoctor.Android.Detection;
using FlutterBuildDoctor.Application.Environment;

namespace FlutterBuildDoctor.IntegrationTests.Environment;

public sealed class AndroidAvdManagerDetectorIntegrationTests
{
    [Fact]
    public void Detect_ComposesFromSdkRootThroughCommandLineToolsWithoutExecutingAvdManager()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlutterBuildDoctorTests", "AvdManagerIntegration", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "platform-tools"));
            var tools = Path.Combine(root, "cmdline-tools", "latest");
            var bin = Path.Combine(tools, "bin");
            Directory.CreateDirectory(bin);
            File.WriteAllText(Path.Combine(tools, "source.properties"), "Pkg.Revision=19.0\n");
            File.WriteAllText(Path.Combine(bin, "sdkmanager.bat"), "fixture");
            var avdManagerPath = Path.Combine(bin, "avdmanager.bat");
            File.WriteAllText(avdManagerPath, "fixture");

            var sdkRootResult = new AndroidSdkRootDetector().Detect(Snapshot(root));
            var commandLineResult = new AndroidCommandLineToolsDetector().Detect(sdkRootResult);
            var result = new AndroidAvdManagerDetector().Detect(commandLineResult);

            Assert.True(sdkRootResult.IsSuccess, sdkRootResult.Message);
            Assert.True(commandLineResult.IsSuccess, commandLineResult.Message);
            Assert.True(result.IsSuccess, result.Message);
            Assert.Equal(avdManagerPath, result.EffectiveCandidate!.AvdManagerPath, ignoreCase: true);
            Assert.Equal("19.0", result.EffectiveCandidate.CommandLineToolsRevision);
            Assert.True(result.EffectiveCandidate.Exists);
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
}
