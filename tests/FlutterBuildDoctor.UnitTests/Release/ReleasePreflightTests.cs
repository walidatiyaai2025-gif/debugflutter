using FlutterBuildDoctor.Flutter.Release;

namespace FlutterBuildDoctor.UnitTests.Release;

public sealed class ReleasePreflightTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"fbd-release-{Guid.NewGuid():N}");

    [Fact]
    public void Inspect_ReadyProjectPassesWithoutLeakingSigningSecrets()
    {
        CreateReadyProject();
        var secret = "SuperSecretStorePassword!";
        File.WriteAllText(Path.Combine(_root, "android", "key.properties"), $"""
            storePassword={secret}
            keyPassword=AnotherSecret!
            keyAlias=upload
            storeFile=../app/upload-keystore.jks
            """);
        var preflight = Service().Inspect(_root);

        Assert.True(preflight.IsReady);
        Assert.Equal(0, preflight.BlockerCount);
        var rendered = string.Join("\n", preflight.Checks.SelectMany(check => check.Evidence).Concat(preflight.Checks.Select(check => check.Summary)));
        Assert.DoesNotContain(secret, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("AnotherSecret!", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Inspect_MissingSigningConfigurationBlocksRelease()
    {
        CreateReadyProject();
        File.Delete(Path.Combine(_root, "android", "key.properties"));

        var preflight = Service().Inspect(_root);

        Assert.False(preflight.IsReady);
        Assert.Contains(preflight.Checks, check => check.Code == "release.signing" && check.Status == ReleaseCheckStatus.Blocker);
    }

    [Fact]
    public void Inspect_InvalidPackageVersionAndDebuggableManifestProduceBlockers()
    {
        CreateReadyProject();
        File.WriteAllText(Path.Combine(_root, "android", "app", "build.gradle.kts"), "android { defaultConfig { applicationId = \"1bad.package\" } }");
        File.WriteAllText(Path.Combine(_root, "pubspec.yaml"), "name: demo\nversion: bad+0\n");
        File.WriteAllText(Path.Combine(_root, "android", "app", "src", "main", "AndroidManifest.xml"), """
            <manifest xmlns:android="http://schemas.android.com/apk/res/android">
              <application android:label="Demo" android:debuggable="true">
                <activity android:name=".MainActivity"><intent-filter>
                  <action android:name="android.intent.action.MAIN" />
                  <category android:name="android.intent.category.LAUNCHER" />
                </intent-filter></activity>
              </application>
            </manifest>
            """);

        var preflight = Service().Inspect(_root);

        Assert.True(preflight.BlockerCount >= 3);
        Assert.Contains(preflight.Checks, check => check.Code == "release.package-id" && check.Status == ReleaseCheckStatus.Blocker);
        Assert.Contains(preflight.Checks, check => check.Code == "release.version" && check.Status == ReleaseCheckStatus.Blocker);
        Assert.Contains(preflight.Checks, check => check.Code == "release.manifest" && check.Status == ReleaseCheckStatus.Blocker);
    }

    private ReleasePreflightService Service()
        => new(
            new ReleasePackageInspector(),
            new ReleaseVersionInspector(),
            new ReleaseSigningInspector(),
            new ReleaseManifestInspector());

    private void CreateReadyProject()
    {
        Directory.CreateDirectory(Path.Combine(_root, "android", "app", "src", "main"));
        File.WriteAllText(Path.Combine(_root, "pubspec.yaml"), "name: demo\nversion: 1.2.3+45\n");
        File.WriteAllText(Path.Combine(_root, "android", "app", "build.gradle.kts"), "android { defaultConfig { applicationId = \"com.example.demo\" } }");
        File.WriteAllText(Path.Combine(_root, "android", "app", "src", "main", "AndroidManifest.xml"), """
            <manifest xmlns:android="http://schemas.android.com/apk/res/android">
              <application android:label="Demo">
                <activity android:name=".MainActivity" android:exported="true"><intent-filter>
                  <action android:name="android.intent.action.MAIN" />
                  <category android:name="android.intent.category.LAUNCHER" />
                </intent-filter></activity>
              </application>
            </manifest>
            """);
        File.WriteAllText(Path.Combine(_root, "android", "app", "upload-keystore.jks"), "not-a-real-keystore");
        File.WriteAllText(Path.Combine(_root, "android", "key.properties"), "storePassword=x\nkeyPassword=y\nkeyAlias=upload\nstoreFile=../app/upload-keystore.jks\n");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
