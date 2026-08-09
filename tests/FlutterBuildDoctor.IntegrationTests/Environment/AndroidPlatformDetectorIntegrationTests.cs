using FlutterBuildDoctor.Android.Detection;
using FlutterBuildDoctor.Application.Environment;

namespace FlutterBuildDoctor.IntegrationTests.Environment;

public sealed class AndroidPlatformDetectorIntegrationTests
{
    [Fact]
    public void Detect_ComposesFromEnvironmentSnapshotThroughSdkRootWithoutMutation()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlutterBuildDoctorTests", "PlatformIntegration", Guid.NewGuid().ToString("N"));
        var beforeSdkRoot = System.Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT", EnvironmentVariableTarget.Process);
        var beforeAndroidHome = System.Environment.GetEnvironmentVariable("ANDROID_HOME", EnvironmentVariableTarget.Process);

        try
        {
            var platform = Path.Combine(root, "platforms", "android-35");
            Directory.CreateDirectory(platform);
            File.WriteAllText(
                Path.Combine(platform, "source.properties"),
                "Pkg.Revision=2\nAndroidVersion.ApiLevel=35\nAndroidVersion.CodeName=REL\n");
            File.WriteAllText(Path.Combine(platform, "android.jar"), "fixture");

            var snapshot = Snapshot(root);
            var sdkRootResult = new AndroidSdkRootDetector().Detect(snapshot);
            var result = new AndroidPlatformDetector().Detect(sdkRootResult);

            Assert.True(sdkRootResult.IsSuccess, sdkRootResult.Message);
            Assert.True(result.IsSuccess, result.Message);
            var installed = Assert.Single(result.Platforms);
            Assert.Equal(35, installed.ApiLevel);
            Assert.Equal("2", installed.Revision);
            Assert.True(installed.AndroidJarExists);
            Assert.Equal(new[] { 35 }, result.InstalledApiLevels);
            Assert.Equal(beforeSdkRoot, System.Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT", EnvironmentVariableTarget.Process));
            Assert.Equal(beforeAndroidHome, System.Environment.GetEnvironmentVariable("ANDROID_HOME", EnvironmentVariableTarget.Process));
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
