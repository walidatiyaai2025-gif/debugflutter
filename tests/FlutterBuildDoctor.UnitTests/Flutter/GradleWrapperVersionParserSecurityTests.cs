using FlutterBuildDoctor.Flutter.ProjectAnalysis;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class GradleWrapperVersionParserSecurityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fbd-gradle-wrapper-security-" + Guid.NewGuid().ToString("N"));
    private readonly GradleWrapperVersionParser _parser = new();

    public GradleWrapperVersionParserSecurityTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ResultContract_DoesNotExposeRawPropertiesText()
    {
        Assert.Null(typeof(GradleWrapperVersionResult).GetProperty("RawText"));
    }

    [Fact]
    public void Parse_CredentialedDistributionUrl_OnlyReturnsSanitizedEvidence()
    {
        WriteProperties("distributionUrl=https\\://build-user:build-password@example.invalid/gradle-8.14-bin.zip?token=private-token#fragment\n");

        var result = _parser.Parse(SuccessfulRoot());

        Assert.True(result.IsSuccess);
        Assert.Equal("8.14", result.Version);
        Assert.Equal("https://example.invalid/gradle-8.14-bin.zip", result.DistributionUrl);
        Assert.DoesNotContain("build-user", result.DistributionUrl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("build-password", result.DistributionUrl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-token", result.DistributionUrl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fragment", result.DistributionUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_InvalidUnicodeEscape_ReturnsInvalidPropertiesWithoutEchoingInput()
    {
        const string secret = "do-not-echo-this-secret";
        WriteProperties($"distributionUrl=https\\://example.invalid/gradle-8.14-bin.zip?token={secret}\\u12ZZ\n");

        var result = _parser.Parse(SuccessfulRoot());

        Assert.Equal(GradleWrapperVersionStatus.InvalidProperties, result.Status);
        Assert.Null(result.DistributionUrl);
        Assert.DoesNotContain(secret, result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_OversizePropertiesFile_IsRejectedBeforeContentParsing()
    {
        var path = PropertiesPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            stream.SetLength((256L * 1024) + 1);

        var result = _parser.Parse(SuccessfulRoot());

        Assert.Equal(GradleWrapperVersionStatus.FileTooLarge, result.Status);
        Assert.Null(result.DistributionUrl);
        Assert.Null(result.Version);
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
        var path = PropertiesPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string PropertiesPath()
        => Path.Combine(_root, "android", "gradle", "wrapper", "gradle-wrapper.properties");

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
