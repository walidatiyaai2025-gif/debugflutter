using FlutterBuildDoctor.Flutter.ProjectAnalysis;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class GradleWrapperVersionParserTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fbd-gradle-wrapper-" + Guid.NewGuid().ToString("N"));
    private readonly GradleWrapperVersionParser _parser = new();

    public GradleWrapperVersionParserTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Parse_StandardEscapedBinUrl_ReturnsVersionAndSanitizedUrl()
    {
        WriteProperties("distributionUrl=https\\://services.gradle.org/distributions/gradle-8.10.2-bin.zip\n");

        var result = _parser.Parse(SuccessfulRoot());

        Assert.True(result.IsSuccess);
        Assert.Equal("8.10.2", result.Version);
        Assert.Equal(GradleDistributionKind.Bin, result.DistributionKind);
        Assert.Equal("https://services.gradle.org/distributions/gradle-8.10.2-bin.zip", result.DistributionUrl);
        Assert.EndsWith(Path.Combine("gradle", "wrapper", "gradle-wrapper.properties"), result.PropertiesPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_AllDistributionAndPrereleaseVersion_ArePreserved()
    {
        WriteProperties("distributionUrl=https\\://services.gradle.org/distributions/gradle-9.0-rc-1-all.zip\n");

        var result = _parser.Parse(SuccessfulRoot());

        Assert.True(result.IsSuccess);
        Assert.Equal("9.0-rc-1", result.Version);
        Assert.Equal(GradleDistributionKind.All, result.DistributionKind);
    }

    [Fact]
    public void Parse_ContinuationLine_IsParsedAsOneJavaProperty()
    {
        WriteProperties(
            "distributionUrl=https\\://services.gradle.org/distributions/\\\n" +
            "  gradle-8.12-all.zip\n");

        var result = _parser.Parse(SuccessfulRoot());

        Assert.True(result.IsSuccess);
        Assert.Equal("8.12", result.Version);
        Assert.Equal(GradleDistributionKind.All, result.DistributionKind);
    }

    [Fact]
    public void Parse_QueryCredentials_AreNotReturnedAsEvidence()
    {
        WriteProperties("distributionUrl=https\\://user:secret@example.invalid/gradle-8.8-bin.zip?token=super-secret\n");

        var result = _parser.Parse(SuccessfulRoot());

        Assert.True(result.IsSuccess);
        Assert.Equal("8.8", result.Version);
        Assert.DoesNotContain("user", result.DistributionUrl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", result.DistributionUrl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", result.DistributionUrl, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("https://example.invalid/gradle-8.8-bin.zip", result.DistributionUrl);
    }

    [Fact]
    public void Parse_MissingPropertiesFile_ReturnsExplicitStatus()
    {
        Directory.CreateDirectory(Path.Combine(_root, "android"));

        var result = _parser.Parse(SuccessfulRoot());

        Assert.Equal(GradleWrapperVersionStatus.WrapperPropertiesMissing, result.Status);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Parse_MissingDistributionUrl_ReturnsExplicitStatus()
    {
        WriteProperties("networkTimeout=10000\nvalidateDistributionUrl=true\n");

        var result = _parser.Parse(SuccessfulRoot());

        Assert.Equal(GradleWrapperVersionStatus.DistributionUrlMissing, result.Status);
        Assert.Null(result.Version);
    }

    [Fact]
    public void Parse_DifferentDuplicateDistributionUrls_DoesNotGuess()
    {
        WriteProperties(
            "distributionUrl=https\\://services.gradle.org/distributions/gradle-8.8-bin.zip\n" +
            "distributionUrl=https\\://services.gradle.org/distributions/gradle-8.9-bin.zip\n");

        var result = _parser.Parse(SuccessfulRoot());

        Assert.Equal(GradleWrapperVersionStatus.DistributionUrlInvalid, result.Status);
        Assert.Null(result.Version);
        Assert.Contains("Multiple", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_ArchiveWithoutGradleVersion_ReturnsVersionNotFound()
    {
        WriteProperties("distributionUrl=https\\://example.invalid/custom-wrapper.zip\n");

        var result = _parser.Parse(SuccessfulRoot());

        Assert.Equal(GradleWrapperVersionStatus.VersionNotFound, result.Status);
        Assert.Null(result.Version);
        Assert.Equal("https://example.invalid/custom-wrapper.zip", result.DistributionUrl);
    }

    [Fact]
    public void Parse_UnsuccessfulProjectRoot_ReturnsProjectRootUnavailable()
    {
        var root = new FlutterProjectRootResult(
            FlutterProjectRootStatus.Ambiguous,
            _root,
            null,
            null,
            Array.Empty<FlutterProjectCandidate>(),
            Array.Empty<string>(),
            "Ambiguous project root.");

        var result = _parser.Parse(root);

        Assert.Equal(GradleWrapperVersionStatus.ProjectRootUnavailable, result.Status);
        Assert.Null(result.PropertiesPath);
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
            // Cleanup must not hide assertion failures.
        }
    }

    private void WriteProperties(string content)
    {
        var path = Path.Combine(_root, "android", "gradle", "wrapper", "gradle-wrapper.properties");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private FlutterProjectRootResult SuccessfulRoot()
        => new(
            FlutterProjectRootStatus.Succeeded,
            _root,
            _root,
            Path.Combine(_root, "pubspec.yaml"),
            Array.Empty<FlutterProjectCandidate>(),
            new[] { Path.Combine(_root, "pubspec.yaml") },
            "Test project root.");
}
