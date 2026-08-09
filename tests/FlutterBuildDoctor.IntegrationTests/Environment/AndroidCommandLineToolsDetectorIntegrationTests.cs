using FlutterBuildDoctor.Android.Detection;
using FlutterBuildDoctor.Application.Environment;

namespace FlutterBuildDoctor.IntegrationTests.Environment;

public sealed class AndroidCommandLineToolsDetectorIntegrationTests
{
    [Fact]
    public void Detect_ComposesWithAndroidSdkRootDetector_OnRealTemporaryLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlutterBuildDoctorTests", "CmdlineIntegration", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "platform-tools"));
            var tools = Path.Combine(root, "cmdline-tools", "latest");
            Directory.CreateDirectory(Path.Combine(tools, "bin"));
            File.WriteAllText(Path.Combine(tools, "source.properties"), "Pkg.Revision=19.0\nPkg.Path=cmdline-tools;latest\n");
            File.WriteAllText(Path.Combine(tools, "bin", "sdkmanager.bat"), "@echo off");

            var snapshot = Snapshot(root);
            var sdkRootResult = new AndroidSdkRootDetector().Detect(snapshot);
            var result = new AndroidCommandLineToolsDetector().Detect(sdkRootResult);

            Assert.True(sdkRootResult.IsSuccess, sdkRootResult.Message);
            Assert.True(result.IsSuccess, result.Message);
            Assert.Equal("19.0", result.EffectiveCandidate!.Revision);
            Assert.EndsWith("sdkmanager.bat", result.EffectiveCandidate.SdkManagerPath!, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(Path.GetFullPath(root), result.AndroidSdkRoot, ignoreCase: true);
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
