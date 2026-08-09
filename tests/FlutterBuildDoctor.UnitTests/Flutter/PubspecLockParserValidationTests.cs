using FlutterBuildDoctor.Flutter.ProjectAnalysis;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class PubspecLockParserValidationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fbd-pubspec-lock-validation-" + Guid.NewGuid().ToString("N"));

    public PubspecLockParserValidationTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Parse_HostedChecksumAndGitRefs_ArePreservedAsResolvedEvidence()
    {
        WriteLock(
            """
            packages:
              hosted_pkg:
                dependency: direct main
                description:
                  name: hosted_pkg
                  sha256: abcdef0123456789
                  url: https://pub.dev
                source: hosted
                version: 1.2.3
              git_pkg:
                dependency: transitive
                description:
                  path: packages/core
                  ref: release/v2
                  resolved-ref: 0123456789abcdef
                  url: https://github.com/example/repo.git
                source: git
                version: 2.0.0
            sdks:
              dart: ">=3.5.0 <4.0.0"
            """);

        var result = new PubspecLockParser().Parse(SuccessfulRoot());

        Assert.True(result.IsSuccess);
        var metadata = Assert.IsType<PubspecLockMetadata>(result.Metadata);
        var hosted = Assert.IsType<PubspecLockedPackage>(metadata.FindPackage("hosted_pkg"));
        Assert.Equal("abcdef0123456789", hosted.Sha256);
        Assert.Equal("direct main", hosted.DependencyType);

        var git = Assert.IsType<PubspecLockedPackage>(metadata.FindPackage("git_pkg"));
        Assert.Equal("release/v2", git.GitRef);
        Assert.Equal("0123456789abcdef", git.GitResolvedRef);
        Assert.Equal("transitive", git.DependencyType);
    }

    [Theory]
    [InlineData("sdks: []")]
    [InlineData("sdks:\n  dart: [\">=3.5.0\"]")]
    public void Parse_SdkSectionHasWrongShape_ReturnsInvalidDocument(string sdkSection)
    {
        WriteLock($"packages: {{}}\n{sdkSection}\n");

        var result = new PubspecLockParser().Parse(SuccessfulRoot());

        Assert.Equal(PubspecLockParseStatus.InvalidDocument, result.Status);
        Assert.Null(result.Metadata);
        Assert.NotNull(result.RawText);
    }

    [Fact]
    public void Parse_PackageMissingDependencyRelationship_ReturnsInvalidDocument()
    {
        WriteLock(
            """
            packages:
              http:
                description:
                  name: http
                  url: https://pub.dev
                source: hosted
                version: 1.2.3
            """);

        var result = new PubspecLockParser().Parse(SuccessfulRoot());

        Assert.Equal(PubspecLockParseStatus.InvalidDocument, result.Status);
        Assert.Contains("dependency relationship", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("[direct, main]", "hosted", "1.2.3")]
    [InlineData("direct main", "[hosted]", "1.2.3")]
    [InlineData("direct main", "hosted", "[1.2.3]")]
    public void Parse_KnownPackageFieldHasWrongShape_ReturnsInvalidDocument(
        string dependencyValue,
        string sourceValue,
        string versionValue)
    {
        WriteLock(
            $"packages:\n" +
            $"  http:\n" +
            $"    dependency: {dependencyValue}\n" +
            $"    description:\n" +
            $"      name: http\n" +
            $"      url: https://pub.dev\n" +
            $"    source: {sourceValue}\n" +
            $"    version: {versionValue}\n");

        var result = new PubspecLockParser().Parse(SuccessfulRoot());

        Assert.Equal(PubspecLockParseStatus.InvalidDocument, result.Status);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public void Parse_DescriptionKnownFieldHasWrongShape_ReturnsInvalidDocument()
    {
        WriteLock(
            """
            packages:
              http:
                dependency: direct main
                description:
                  name: http
                  sha256: [abc]
                  url: https://pub.dev
                source: hosted
                version: 1.2.3
            """);

        var result = new PubspecLockParser().Parse(SuccessfulRoot());

        Assert.Equal(PubspecLockParseStatus.InvalidDocument, result.Status);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public void Parse_LockFileExceedsSafetyLimit_ReturnsFileTooLargeWithoutRawRead()
    {
        var path = Path.Combine(_root, "pubspec.lock");
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.SetLength((8L * 1024 * 1024) + 1);
        }

        var result = new PubspecLockParser().Parse(SuccessfulRoot());

        Assert.Equal(PubspecLockParseStatus.FileTooLarge, result.Status);
        Assert.Null(result.RawText);
    }

    [Fact]
    public void Parse_LockFileSymlinkOrReparsePoint_IsRejectedWhenPlatformSupportsIt()
    {
        var outsideDirectory = Path.Combine(Path.GetTempPath(), "fbd-lock-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideDirectory);
        var outsideFile = Path.Combine(outsideDirectory, "outside.lock");
        File.WriteAllText(outsideFile, "packages: {}\nPRIVATE_OUTSIDE_MARKER\n");
        var lockPath = Path.Combine(_root, "pubspec.lock");

        try
        {
            try
            {
                File.CreateSymbolicLink(lockPath, outsideFile);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
            {
                return;
            }

            if ((File.GetAttributes(lockPath) & FileAttributes.ReparsePoint) == 0)
                return;

            var result = new PubspecLockParser().Parse(SuccessfulRoot());

            Assert.Equal(PubspecLockParseStatus.InvalidRequest, result.Status);
            Assert.Null(result.Metadata);
            Assert.Null(result.RawText);
            Assert.Contains("reparse", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                if (File.Exists(lockPath)) File.Delete(lockPath);
                if (Directory.Exists(outsideDirectory)) Directory.Delete(outsideDirectory, recursive: true);
            }
            catch
            {
                // Cleanup must not hide assertion failures.
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
            // Cleanup must not hide assertion failures.
        }
    }

    private void WriteLock(string content)
        => File.WriteAllText(Path.Combine(_root, "pubspec.lock"), content);

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
