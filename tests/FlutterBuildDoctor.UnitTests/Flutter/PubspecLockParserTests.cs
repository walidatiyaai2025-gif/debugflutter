using FlutterBuildDoctor.Flutter.ProjectAnalysis;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class PubspecLockParserTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fbd-pubspec-lock-" + Guid.NewGuid().ToString("N"));
    private readonly PubspecLockParser _parser = new();

    public PubspecLockParserTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Parse_ValidLock_ReadsLockedVersionsSourcesAndSdkConstraints()
    {
        WriteLock(
            """
            packages:
              http:
                dependency: direct main
                description:
                  name: http
                  sha256: abc
                  url: https://pub.dev
                source: hosted
                version: 1.2.2
              local_package:
                dependency: direct main
                description:
                  path: ../local_package
                  relative: true
                source: path
                version: 1.0.0
              git_package:
                dependency: transitive
                description:
                  path: packages/core
                  ref: main
                  resolved-ref: abcdef123456
                  url: https://github.com/example/repo.git
                source: git
                version: 2.0.0
              flutter:
                dependency: direct main
                description: flutter
                source: sdk
                version: 0.0.0
            sdks:
              dart: ">=3.5.0 <4.0.0"
              flutter: ">=3.24.0"
            """);

        var result = _parser.Parse(SuccessfulRoot());

        Assert.True(result.IsSuccess);
        var metadata = Assert.IsType<PubspecLockMetadata>(result.Metadata);
        Assert.Equal(">=3.5.0 <4.0.0", metadata.DartSdkConstraint);
        Assert.Equal(">=3.24.0", metadata.FlutterSdkConstraint);
        Assert.Equal(4, metadata.Packages.Count);

        var http = Assert.IsType<PubspecLockedPackage>(metadata.FindPackage("http"));
        Assert.Equal("1.2.2", http.Version);
        Assert.Equal(PubspecLockedPackageSource.Hosted, http.Source);
        Assert.Equal("direct main", http.DependencyType);
        Assert.Equal("https://pub.dev", http.DescriptionUrl?.TrimEnd('/'));

        var local = Assert.IsType<PubspecLockedPackage>(metadata.FindPackage("local_package"));
        Assert.Equal(PubspecLockedPackageSource.Path, local.Source);
        Assert.Equal("../local_package", local.DescriptionPath);

        var git = Assert.IsType<PubspecLockedPackage>(metadata.FindPackage("git_package"));
        Assert.Equal(PubspecLockedPackageSource.Git, git.Source);
        Assert.Equal("abcdef123456", git.GitResolvedRef);
        Assert.Equal("https://github.com/example/repo.git", git.GitUrl?.TrimEnd('/'));
        Assert.Equal("packages/core", git.DescriptionPath);

        var flutter = Assert.IsType<PubspecLockedPackage>(metadata.FindPackage("flutter"));
        Assert.Equal(PubspecLockedPackageSource.Sdk, flutter.Source);
        Assert.Equal("flutter", flutter.DescriptionName);
    }

    [Fact]
    public void Parse_MissingLock_DoesNotRunDependencyResolution()
    {
        var result = _parser.Parse(SuccessfulRoot());

        Assert.Equal(PubspecLockParseStatus.LockFileNotFound, result.Status);
        Assert.Null(result.Metadata);
        Assert.Contains("No package resolution command was run", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_MalformedYaml_PreservesSourceEvidence()
    {
        WriteLock("packages:\n  broken: [one, two\n");

        var result = _parser.Parse(SuccessfulRoot());

        Assert.Equal(PubspecLockParseStatus.MalformedYaml, result.Status);
        Assert.NotNull(result.RawText);
        Assert.Contains("broken", result.RawText, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_PackageMissingVersion_ReturnsInvalidDocument()
    {
        WriteLock(
            """
            packages:
              http:
                dependency: direct main
                description:
                  name: http
                  url: https://pub.dev
                source: hosted
            """);

        var result = _parser.Parse(SuccessfulRoot());

        Assert.Equal(PubspecLockParseStatus.InvalidDocument, result.Status);
        Assert.Contains("locked version", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_RedactsCredentialsFromStructuredUrls()
    {
        WriteLock(
            """
            packages:
              private_git:
                dependency: direct main
                description:
                  resolved-ref: abc123
                  url: https://user:secret@github.com/example/private.git?token=query-secret#ref
                source: git
                version: 1.0.0
              private_hosted:
                dependency: transitive
                description:
                  name: private_hosted
                  url: https://registry-user:registry-secret@packages.example.com/api?token=hidden
                source: hosted
                version: 2.0.0
            """);

        var result = _parser.Parse(SuccessfulRoot());

        Assert.True(result.IsSuccess);
        var metadata = Assert.IsType<PubspecLockMetadata>(result.Metadata);
        var git = Assert.IsType<PubspecLockedPackage>(metadata.FindPackage("private_git"));
        Assert.DoesNotContain("secret", git.GitUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("query-secret", git.GitUrl, StringComparison.Ordinal);
        Assert.Contains("github.com", git.GitUrl, StringComparison.OrdinalIgnoreCase);

        var hosted = Assert.IsType<PubspecLockedPackage>(metadata.FindPackage("private_hosted"));
        Assert.DoesNotContain("registry-secret", hosted.DescriptionUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden", hosted.DescriptionUrl, StringComparison.Ordinal);
        Assert.Contains("packages.example.com", hosted.DescriptionUrl, StringComparison.OrdinalIgnoreCase);
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
            "Ambiguous root.");

        var result = _parser.Parse(root);

        Assert.Equal(PubspecLockParseStatus.ProjectRootUnavailable, result.Status);
        Assert.Null(result.LockFilePath);
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
