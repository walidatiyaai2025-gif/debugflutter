using FlutterBuildDoctor.Flutter.ProjectAnalysis;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class GradleWrapperParserTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fbd-gradle-wrapper-" + Guid.NewGuid().ToString("N"));

    public GradleWrapperParserTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("https\\://services.gradle.org/distributions/gradle-8.14-bin.zip", "8.14", GradleDistributionType.Bin)]
    [InlineData("https\\://services.gradle.org/distributions/gradle-8.14-all.zip", "8.14", GradleDistributionType.All)]
    [InlineData("https\\://services.gradle.org/distributions/gradle-9.0.0-rc-1-bin.zip", "9.0.0-rc-1", GradleDistributionType.Bin)]
    public void Parse_StandardDistributionUrl_ExtractsVersionAndDistributionType(
        string distributionUrl,
        string expectedVersion,
        GradleDistributionType expectedType)
    {
        WriteProperties($"distributionUrl={distributionUrl}\n");

        var result = new GradleWrapperParser().Parse(SuccessfulRoot());

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedVersion, result.GradleVersion);
        Assert.Equal(expectedType, result.DistributionType);
        Assert.Contains("services.gradle.org", result.DistributionUrl, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.RawText);
    }

    [Fact]
    public void Parse_JavaPropertiesContinuationAndEscapes_AreHandled()
    {
        WriteProperties(
            "# Gradle wrapper fixture\n" +
            "distributionUrl=https\\://services.gradle.org/distributions/\\\n" +
            "  gradle-8.12.1-all.zip\n");

        var result = new GradleWrapperParser().Parse(SuccessfulRoot());

        Assert.True(result.IsSuccess);
        Assert.Equal("8.12.1", result.GradleVersion);
        Assert.Equal(GradleDistributionType.All, result.DistributionType);
    }

    [Fact]
    public void Parse_DuplicateDistributionUrl_UsesLastValueLikeJavaProperties()
    {
        WriteProperties(
            "distributionUrl=https\\://services.gradle.org/distributions/gradle-8.10-bin.zip\n" +
            "distributionUrl=https\\://services.gradle.org/distributions/gradle-8.14-bin.zip\n");

        var result = new GradleWrapperParser().Parse(SuccessfulRoot());

        Assert.True(result.IsSuccess);
        Assert.Equal("8.14", result.GradleVersion);
    }

    [Fact]
    public void Parse_DistributionUrlCredentialsQueryAndFragment_AreRemovedFromStructuredEvidence()
    {
        WriteProperties("distributionUrl=https\\://user:secret@example.com/gradle-8.14-bin.zip?token=abc#fragment\n");

        var result = new GradleWrapperParser().Parse(SuccessfulRoot());

        Assert.True(result.IsSuccess);
        Assert.Equal("8.14", result.GradleVersion);
        Assert.DoesNotContain("user", result.DistributionUrl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", result.DistributionUrl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", result.DistributionUrl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fragment", result.DistributionUrl, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("user:secret", result.RawText, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_MissingDistributionUrl_ReturnsExplicitStatusWithRawEvidence()
    {
        WriteProperties("distributionBase=GRADLE_USER_HOME\n");

        var result = new GradleWrapperParser().Parse(SuccessfulRoot());

        Assert.Equal(GradleWrapperParseStatus.DistributionUrlMissing, result.Status);
        Assert.NotNull(result.RawText);
        Assert.Null(result.GradleVersion);
    }

    [Fact]
    public void Parse_NonStandardDistributionFile_ReturnsVersionNotDetected()
    {
        WriteProperties("distributionUrl=https\\://example.com/custom-wrapper.zip\n");

        var result = new GradleWrapperParser().Parse(SuccessfulRoot());

        Assert.Equal(GradleWrapperParseStatus.VersionNotDetected, result.Status);
        Assert.Equal("https://example.com/custom-wrapper.zip", result.DistributionUrl);
        Assert.NotNull(result.RawText);
    }

    [Fact]
    public void Parse_InvalidUnicodeEscape_ReturnsInvalidPropertiesAndPreservesRawEvidence()
    {
        WriteProperties("distributionUrl=https\\://example.com/gradle-8.14-bin.zip\\u12ZZ\n");

        var result = new GradleWrapperParser().Parse(SuccessfulRoot());

        Assert.Equal(GradleWrapperParseStatus.InvalidProperties, result.Status);
        Assert.NotNull(result.RawText);
    }

    [Fact]
    public void Parse_MissingWrapperDirectory_DoesNotCreateOrRepairIt()
    {
        var expectedWrapperDirectory = Path.Combine(_root, "android", "gradle", "wrapper");

        var result = new GradleWrapperParser().Parse(SuccessfulRoot());

        Assert.Equal(GradleWrapperParseStatus.WrapperDirectoryMissing, result.Status);
        Assert.False(Directory.Exists(expectedWrapperDirectory));
        Assert.Null(result.RawText);
    }

    [Fact]
    public void Parse_MissingPropertiesFile_ReturnsExplicitStatus()
    {
        Directory.CreateDirectory(WrapperDirectory());

        var result = new GradleWrapperParser().Parse(SuccessfulRoot());

        Assert.Equal(GradleWrapperParseStatus.PropertiesFileMissing, result.Status);
        Assert.Null(result.RawText);
    }

    [Fact]
    public void Parse_OversizePropertiesFile_IsRejectedBeforeReading()
    {
        Directory.CreateDirectory(WrapperDirectory());
        var path = PropertiesPath();
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            stream.SetLength((256L * 1024) + 1);

        var result = new GradleWrapperParser().Parse(SuccessfulRoot());

        Assert.Equal(GradleWrapperParseStatus.FileTooLarge, result.Status);
        Assert.Null(result.RawText);
    }

    [Fact]
    public void Parse_PropertiesSymlink_IsRejectedWhenPlatformSupportsIt()
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

            var result = new GradleWrapperParser().Parse(SuccessfulRoot());

            Assert.Equal(GradleWrapperParseStatus.UnsafePath, result.Status);
            Assert.Null(result.RawText);
            Assert.Null(result.GradleVersion);
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
    public void Parse_UnsuccessfulProjectRoot_ReturnsProjectRootUnavailable()
    {
        var unavailable = new FlutterProjectRootResult(
            FlutterProjectRootStatus.Ambiguous,
            _root,
            null,
            null,
            Array.Empty<FlutterProjectCandidate>(),
            Array.Empty<string>(),
            "Ambiguous.");

        var result = new GradleWrapperParser().Parse(unavailable);

        Assert.Equal(GradleWrapperParseStatus.ProjectRootUnavailable, result.Status);
        Assert.Null(result.RawText);
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
