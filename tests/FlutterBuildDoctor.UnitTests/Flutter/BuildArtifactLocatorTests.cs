using FlutterBuildDoctor.Flutter.Build;
using FlutterBuildDoctor.Flutter.Commands;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class BuildArtifactLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"fbd-build-{Guid.NewGuid():N}");

    [Fact]
    public void Locate_PrefersExpectedFlutterApkPath()
    {
        var expected = Path.Combine(_root, "build", "app", "outputs", "flutter-apk", "app-release.apk");
        Directory.CreateDirectory(Path.GetDirectoryName(expected)!);
        File.WriteAllText(expected, "apk-data");
        var locator = new BuildArtifactLocator();

        var artifact = locator.Locate(Request(FlutterBuildArtifactType.Apk, FlutterBuildMode.Release));

        Assert.NotNull(artifact);
        Assert.Equal(Path.GetFullPath(expected), artifact!.Path);
        Assert.Equal(new FileInfo(expected).Length, artifact.SizeBytes);
    }

    [Fact]
    public void Locate_FallsBackToNewestMatchingArtifactUnderBuildOnly()
    {
        var oldPath = Path.Combine(_root, "build", "custom", "app-qa-release.apk");
        var newPath = Path.Combine(_root, "build", "other", "app-qa-release-copy.apk");
        Directory.CreateDirectory(Path.GetDirectoryName(oldPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
        File.WriteAllText(oldPath, "old");
        File.WriteAllText(newPath, "new");
        File.SetLastWriteTimeUtc(oldPath, DateTime.UtcNow.AddMinutes(-5));
        File.SetLastWriteTimeUtc(newPath, DateTime.UtcNow);
        var locator = new BuildArtifactLocator();

        var artifact = locator.Locate(Request(
            FlutterBuildArtifactType.Apk,
            FlutterBuildMode.Release,
            "qa"));

        Assert.NotNull(artifact);
        Assert.Equal(Path.GetFullPath(newPath), artifact!.Path);
    }

    private FlutterBuildRequest Request(
        FlutterBuildArtifactType type,
        FlutterBuildMode mode,
        string? flavor = null)
        => new(new FlutterCommandContext("flutter", _root), type, mode, flavor);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
