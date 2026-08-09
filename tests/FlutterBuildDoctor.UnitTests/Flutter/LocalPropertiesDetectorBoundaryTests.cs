using FlutterBuildDoctor.Flutter.ProjectAnalysis;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class LocalPropertiesDetectorBoundaryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "fbd-local-properties-boundary-" + Guid.NewGuid().ToString("N"));

    public LocalPropertiesDetectorBoundaryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Detect_UnsuccessfulProjectRoot_IsRejected()
    {
        var failed = new FlutterProjectRootResult(
            FlutterProjectRootStatus.NotFlutterProject,
            _root,
            null,
            null,
            Array.Empty<FlutterProjectCandidate>(),
            Array.Empty<string>(),
            "not flutter");

        var result = new LocalPropertiesDetector().Detect(failed);

        Assert.Equal(LocalPropertiesDetectionStatus.ProjectRootUnavailable, result.Status);
        Assert.Equal(LocalPropertiesPathStatus.MissingKey, result.AndroidSdk.Status);
        Assert.Equal(LocalPropertiesPathStatus.MissingKey, result.FlutterSdk.Status);
    }

    [Fact]
    public void Detect_StaleProjectRoot_IsRejected()
    {
        var missing = Path.Combine(_root, "missing");

        var result = new LocalPropertiesDetector().Detect(SuccessfulRoot(missing));

        Assert.Equal(LocalPropertiesDetectionStatus.ProjectRootUnavailable, result.Status);
    }

    [Fact]
    public void Detect_MissingAndroidDirectory_IsTypedExplicitly()
    {
        var result = new LocalPropertiesDetector().Detect(SuccessfulRoot(_root));

        Assert.Equal(LocalPropertiesDetectionStatus.AndroidDirectoryUnavailable, result.Status);
    }

    [Fact]
    public void Detect_MissingLocalProperties_IsTypedWithoutFallbackInference()
    {
        Directory.CreateDirectory(Path.Combine(_root, "android"));

        var result = new LocalPropertiesDetector().Detect(SuccessfulRoot(_root));

        Assert.True(result.IsSuccess);
        Assert.Equal(LocalPropertiesDetectionStatus.FileMissing, result.Status);
        Assert.Equal(LocalPropertiesPathStatus.MissingKey, result.AndroidSdk.Status);
        Assert.Equal(LocalPropertiesPathStatus.MissingKey, result.FlutterSdk.Status);
    }

    [Fact]
    public void Detect_OversizeLocalProperties_IsRejectedBeforeParsing()
    {
        var android = Path.Combine(_root, "android");
        Directory.CreateDirectory(android);
        var path = Path.Combine(android, "local.properties");
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            stream.SetLength((256L * 1024) + 1);

        var result = new LocalPropertiesDetector().Detect(SuccessfulRoot(_root));

        Assert.Equal(LocalPropertiesDetectionStatus.FileTooLarge, result.Status);
        Assert.Empty(result.AndroidSdk.Evidence);
        Assert.Empty(result.FlutterSdk.Evidence);
    }

    [Fact]
    public void Detect_LocalPropertiesSymlink_IsRejectedWhenSupported()
    {
        var android = Path.Combine(_root, "android");
        Directory.CreateDirectory(android);
        var outside = Path.Combine(
            Path.GetTempPath(),
            "fbd-local-properties-outside-" + Guid.NewGuid().ToString("N") + ".properties");
        File.WriteAllText(outside, "sdk.dir=C:\\\\outside");
        var link = Path.Combine(android, "local.properties");

        try
        {
            try
            {
                File.CreateSymbolicLink(link, outside);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
            {
                return;
            }

            if ((File.GetAttributes(link) & FileAttributes.ReparsePoint) == 0)
                return;

            var result = new LocalPropertiesDetector().Detect(SuccessfulRoot(_root));

            Assert.Equal(LocalPropertiesDetectionStatus.UnsafePath, result.Status);
            Assert.Empty(result.AndroidSdk.Evidence);
            Assert.Empty(result.FlutterSdk.Evidence);
        }
        finally
        {
            try
            {
                if (File.Exists(link)) File.Delete(link);
                if (File.Exists(outside)) File.Delete(outside);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void Detect_AndroidDirectorySymlink_IsRejectedWhenSupported()
    {
        var outside = Path.Combine(
            Path.GetTempPath(),
            "fbd-local-properties-android-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "local.properties"), "sdk.dir=C:\\\\outside");
        var link = Path.Combine(_root, "android");

        try
        {
            try
            {
                Directory.CreateSymbolicLink(link, outside);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
            {
                return;
            }

            if ((File.GetAttributes(link) & FileAttributes.ReparsePoint) == 0)
                return;

            var result = new LocalPropertiesDetector().Detect(SuccessfulRoot(_root));

            Assert.Equal(LocalPropertiesDetectionStatus.UnsafePath, result.Status);
            Assert.Empty(result.AndroidSdk.Evidence);
            Assert.Empty(result.FlutterSdk.Evidence);
        }
        finally
        {
            try
            {
                if (Directory.Exists(link)) Directory.Delete(link);
                if (Directory.Exists(outside)) Directory.Delete(outside, recursive: true);
            }
            catch
            {
            }
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }

    private static FlutterProjectRootResult SuccessfulRoot(string root)
        => new(
            FlutterProjectRootStatus.Succeeded,
            root,
            root,
            Path.Combine(root, "pubspec.yaml"),
            Array.Empty<FlutterProjectCandidate>(),
            new[] { Path.Combine(root, "pubspec.yaml") },
            "Test root.");
}
