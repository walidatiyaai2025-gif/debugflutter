using FlutterBuildDoctor.Flutter.ProjectAnalysis;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class GradleWrapperVersionParserBoundaryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fbd-gradle-wrapper-boundary-" + Guid.NewGuid().ToString("N"));

    public GradleWrapperVersionParserBoundaryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Parse_PropertiesSymlink_IsRejectedWhenSupported()
    {
        Directory.CreateDirectory(WrapperDirectory());
        var outside = Path.Combine(Path.GetTempPath(), "fbd-wrapper-outside-" + Guid.NewGuid().ToString("N") + ".properties");
        File.WriteAllText(outside, "distributionUrl=https\\://services.gradle.org/distributions/gradle-8.14-bin.zip\n");
        var link = PropertiesPath();

        try
        {
            try { File.CreateSymbolicLink(link, outside); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException) { return; }

            if ((File.GetAttributes(link) & FileAttributes.ReparsePoint) == 0)
                return;

            var result = new GradleWrapperVersionParser().Parse(SuccessfulRoot());

            Assert.Equal(GradleWrapperVersionStatus.UnsafePath, result.Status);
            Assert.Null(result.DistributionUrl);
            Assert.Null(result.Version);
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
    public void Parse_AndroidDirectorySymlink_IsRejectedWhenSupported()
    {
        var outside = Path.Combine(Path.GetTempPath(), "fbd-wrapper-android-outside-" + Guid.NewGuid().ToString("N"));
        var outsideWrapper = Path.Combine(outside, "gradle", "wrapper");
        Directory.CreateDirectory(outsideWrapper);
        File.WriteAllText(
            Path.Combine(outsideWrapper, "gradle-wrapper.properties"),
            "distributionUrl=https\\://services.gradle.org/distributions/gradle-8.14-bin.zip\n");
        var android = Path.Combine(_root, "android");

        try
        {
            try { Directory.CreateSymbolicLink(android, outside); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException) { return; }

            if ((File.GetAttributes(android) & FileAttributes.ReparsePoint) == 0)
                return;

            var result = new GradleWrapperVersionParser().Parse(SuccessfulRoot());

            Assert.Equal(GradleWrapperVersionStatus.UnsafePath, result.Status);
            Assert.Null(result.DistributionUrl);
            Assert.Null(result.Version);
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

    private string WrapperDirectory() => Path.Combine(_root, "android", "gradle", "wrapper");
    private string PropertiesPath() => Path.Combine(WrapperDirectory(), "gradle-wrapper.properties");

    private FlutterProjectRootResult SuccessfulRoot() => new(
        FlutterProjectRootStatus.Succeeded,
        _root,
        _root,
        Path.Combine(_root, "pubspec.yaml"),
        Array.Empty<FlutterProjectCandidate>(),
        new[] { Path.Combine(_root, "pubspec.yaml") },
        "Test project root.");
}
