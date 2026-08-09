using FlutterBuildDoctor.Android.Detection;
using FlutterBuildDoctor.Application.Environment;

namespace FlutterBuildDoctor.IntegrationTests.Environment;

public sealed class AndroidBuildToolsDetectorIntegrationTests
{
    [Fact]
    public void Detect_ComposesFromEnvironmentSnapshotThroughSdkRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlutterBuildDoctorTests", "BuildToolsIntegration", Guid.NewGuid().ToString("N"));
        try
        {
            var package = Path.Combine(root, "build-tools", "35.0.0");
            Directory.CreateDirectory(package);
            File.WriteAllText(Path.Combine(package, "source.properties"), "Pkg.Revision=35.0.0\n");
            File.WriteAllText(Path.Combine(package, "aapt2.exe"), "fixture");
            File.WriteAllText(Path.Combine(package, "zipalign.exe"), "fixture");
            File.WriteAllText(Path.Combine(package, "d8.bat"), "fixture");
            File.WriteAllText(Path.Combine(package, "apksigner.bat"), "fixture");

            var sdkRootResult = new AndroidSdkRootDetector().Detect(Snapshot(root));
            var result = new AndroidBuildToolsDetector().Detect(sdkRootResult);

            Assert.True(sdkRootResult.IsSuccess, sdkRootResult.Message);
            Assert.True(result.IsSuccess, result.Message);
            var detected = Assert.Single(result.Packages);
            Assert.Equal("35.0.0", detected.Revision);
            Assert.True(detected.IsUsable);
            Assert.Equal(new[] { "35.0.0" }, result.InstalledVersions);
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
