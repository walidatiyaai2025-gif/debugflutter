using FlutterBuildDoctor.Flutter.ProjectAnalysis;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class GradleWrapperVersionParserBoundaryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fbd-gradle-wrapper-boundary-" + Guid.NewGuid().ToString("N"));

    public GradleWrapperVersionParserBoundaryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Parse_SuccessAndParseFailures_PreserveBoundedRawEvidence()
    {
        const string successRaw = "distributionUrl=https\\://services.gradle.org/distributions/gradle-8.14-bin.zip\n";
        WriteProperties(successRaw);

        var success = new GradleWrapperVersionParser().Parse(SuccessfulRoot());

        Assert.True(success.IsSuccess);
        Assert.Equal(successRaw, success.RawText);

        const string invalidRaw = "distributionUrl=https\\://example.invalid/gradle-8.14-bin.zip\\u12ZZ\n";
        WriteProperties(invalidRaw);

        var invalid = new GradleWrapperVersionParser().Parse(SuccessfulRoot());

        Assert.Equal(GradleWrapperVersionStatus.InvalidProperties, invalid.Status);
        Assert.Equal(invalidRaw, invalid.RawText);
    }

    [Fact]
    public void Parse_OversizePropertiesFile_IsRejectedBeforeRawRead()
    {
        Directory.CreateDirectory(WrapperDirectory());
        using (var stream = new FileStream(PropertiesPath(), FileMode.Create, FileAccess.Write, FileShare.None))
            stream.SetLength((256L * 1024) + 1);

        var result = new GradleWrapperVersionParser().Parse(SuccessfulRoot());

        Assert.Equal(GradleWrapperVersionStatus.FileTooLarge, result.Status);
        Assert.Null(result.RawText);
    }

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
            Assert.Null(result.RawText);
            Assert.Null(result.Version);
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

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { }
    }

    private void WriteProperties(string content)
    {
        Directory.CreateDirectory(WrapperDirectory());
        File.WriteAllText(PropertiesPath(), content);
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
