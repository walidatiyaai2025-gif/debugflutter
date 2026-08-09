using FlutterBuildDoctor.Flutter.ProjectAnalysis;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class GradleDslDetectorBoundaryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fbd-gradle-dsl-boundary-" + Guid.NewGuid().ToString("N"));

    public GradleDslDetectorBoundaryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Detect_GradleScriptSymlink_IsNotSelectedForDownstreamParsing()
    {
        var android = Path.Combine(_root, "android");
        Directory.CreateDirectory(Path.Combine(android, "app"));
        File.WriteAllText(Path.Combine(android, "build.gradle"), "// project");
        File.WriteAllText(Path.Combine(android, "app", "build.gradle"), "// app");

        var outside = Path.Combine(Path.GetTempPath(), "fbd-gradle-outside-" + Guid.NewGuid().ToString("N") + ".gradle");
        File.WriteAllText(outside, "// outside");
        var link = Path.Combine(android, "settings.gradle");

        try
        {
            try { File.CreateSymbolicLink(link, outside); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException) { return; }

            if ((File.GetAttributes(link) & FileAttributes.ReparsePoint) == 0)
                return;

            var result = new GradleDslDetector().Detect(SuccessfulRoot());

            Assert.Equal(GradleDslDetectionStatus.InspectionFailed, result.Status);
            Assert.False(result.IsSuccess);
            Assert.DoesNotContain(result.Scripts, script => string.Equals(script.Path, link, StringComparison.OrdinalIgnoreCase));
            Assert.Contains("reparse", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                if (File.Exists(link)) File.Delete(link);
                if (File.Exists(outside)) File.Delete(outside);
            }
            catch { }
        }
    }

    [Fact]
    public void Detect_AndroidDirectorySymlink_IsNotTraversed()
    {
        var outside = Path.Combine(Path.GetTempPath(), "fbd-android-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "build.gradle"), "// outside");
        var android = Path.Combine(_root, "android");

        try
        {
            try { Directory.CreateSymbolicLink(android, outside); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException) { return; }

            if ((File.GetAttributes(android) & FileAttributes.ReparsePoint) == 0)
                return;

            var result = new GradleDslDetector().Detect(SuccessfulRoot());

            Assert.Equal(GradleDslDetectionStatus.InspectionFailed, result.Status);
            Assert.Empty(result.Scripts);
            Assert.Contains("reparse", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                if (Directory.Exists(android)) Directory.Delete(android);
                if (Directory.Exists(outside)) Directory.Delete(outside, recursive: true);
            }
            catch { }
        }
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { }
    }

    private FlutterProjectRootResult SuccessfulRoot() => new(
        FlutterProjectRootStatus.Succeeded,
        _root,
        _root,
        Path.Combine(_root, "pubspec.yaml"),
        Array.Empty<FlutterProjectCandidate>(),
        new[] { Path.Combine(_root, "pubspec.yaml") },
        "Test project root.");
}
