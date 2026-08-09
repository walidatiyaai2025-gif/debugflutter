using FlutterBuildDoctor.Flutter.ProjectAnalysis;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class PubspecParserValidationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fbd-pubspec-validation-" + Guid.NewGuid().ToString("N"));

    public PubspecParserValidationTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Parse_ValidFlutterPubspec_ExtractsIdentityConstraintsAndDependencyKinds()
    {
        var result = Parse(
            """
            name: sample_app
            description: Sample app
            version: 1.2.3+4
            environment:
              sdk: ">=3.4.0 <4.0.0"
              flutter: ">=3.22.0"
            topics:
              - flutter
              - tooling
            dependencies:
              flutter:
                sdk: flutter
              http: ^1.2.0
              local_pkg:
                path: ../local_pkg
              git_pkg:
                git:
                  url: https://github.com/example/repo.git
                  ref: main
            dev_dependencies:
              flutter_test:
                sdk: flutter
            dependency_overrides:
              http: 1.2.1
            """);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Metadata);
        Assert.Equal("sample_app", result.Metadata.Name);
        Assert.Equal("1.2.3+4", result.Metadata.Version);
        Assert.Equal(">=3.4.0 <4.0.0", result.Metadata.DartSdkConstraint);
        Assert.Equal(">=3.22.0", result.Metadata.FlutterSdkConstraint);
        Assert.Equal(new[] { "flutter", "tooling" }, result.Metadata.Topics);
        Assert.True(result.Metadata.HasFlutterSdkDependency);
        Assert.Contains(result.Metadata.Dependencies, dependency => dependency.Name == "http" && dependency.Kind == PubspecDependencyKind.Hosted);
        Assert.Contains(result.Metadata.Dependencies, dependency => dependency.Name == "local_pkg" && dependency.Kind == PubspecDependencyKind.Path);
        Assert.Contains(result.Metadata.Dependencies, dependency => dependency.Name == "git_pkg" && dependency.Kind == PubspecDependencyKind.Git && dependency.GitRef == "main");
        Assert.Contains(result.Metadata.Dependencies, dependency => dependency.Name == "flutter_test" && dependency.Section == PubspecDependencySection.DevDependencies);
        Assert.Contains(result.Metadata.Dependencies, dependency => dependency.Name == "http" && dependency.Section == PubspecDependencySection.DependencyOverrides);
        Assert.Contains("name: sample_app", result.RawText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("environment: []")]
    [InlineData("dependencies: [http]")]
    [InlineData("dev_dependencies: flutter_test")]
    [InlineData("dependency_overrides: [http]")]
    [InlineData("topics: {flutter: true}")]
    public void Parse_KnownSectionHasWrongYamlShape_ReturnsInvalidDocument(string invalidSection)
    {
        var result = Parse($"name: sample_app\n{invalidSection}\n");

        Assert.Equal(PubspecParseStatus.InvalidDocument, result.Status);
        Assert.Null(result.Metadata);
        Assert.NotNull(result.RawText);
        Assert.Contains(invalidSection, result.RawText, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_DependencySpecIsSequence_ReturnsInvalidDocument()
    {
        var result = Parse(
            """
            name: sample_app
            dependencies:
              http:
                - ^1.2.0
            """);

        Assert.Equal(PubspecParseStatus.InvalidDocument, result.Status);
        Assert.Null(result.Metadata);
        Assert.NotNull(result.RawText);
    }

    [Fact]
    public void Parse_MalformedYaml_PreservesRawEvidence()
    {
        const string raw = "name: sample_app\ndependencies: [unterminated\n";
        var result = Parse(raw);

        Assert.Equal(PubspecParseStatus.MalformedYaml, result.Status);
        Assert.Equal(raw, result.RawText);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public void Parse_ProjectRootUnavailable_DoesNotReadPubspec()
    {
        var unavailable = new FlutterProjectRootResult(
            FlutterProjectRootStatus.Ambiguous,
            _root,
            null,
            null,
            Array.Empty<FlutterProjectCandidate>(),
            Array.Empty<string>(),
            "Ambiguous test root.");

        var result = new PubspecParser().Parse(unavailable);

        Assert.Equal(PubspecParseStatus.ProjectRootUnavailable, result.Status);
        Assert.Null(result.RawText);
    }

    [Fact]
    public void Parse_FileExceedsSafetyLimit_ReturnsFileTooLargeWithoutReadingIt()
    {
        var pubspecPath = Path.Combine(_root, "pubspec.yaml");
        using (var stream = new FileStream(pubspecPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.SetLength((2L * 1024 * 1024) + 1);
        }

        var result = new PubspecParser().Parse(SuccessfulRoot(pubspecPath));

        Assert.Equal(PubspecParseStatus.FileTooLarge, result.Status);
        Assert.Null(result.RawText);
    }

    [Fact]
    public void Parse_PubspecSymlinkOrReparsePoint_IsRejectedWhenPlatformSupportsIt()
    {
        var outsideDirectory = Path.Combine(Path.GetTempPath(), "fbd-pubspec-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideDirectory);
        var outsideFile = Path.Combine(outsideDirectory, "outside.yaml");
        File.WriteAllText(outsideFile, "name: outside_secret\n");
        var pubspecPath = Path.Combine(_root, "pubspec.yaml");

        try
        {
            try
            {
                File.CreateSymbolicLink(pubspecPath, outsideFile);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
            {
                return;
            }

            var attributes = File.GetAttributes(pubspecPath);
            if ((attributes & FileAttributes.ReparsePoint) == 0)
                return;

            var result = new PubspecParser().Parse(SuccessfulRoot(pubspecPath));

            Assert.False(result.IsSuccess);
            Assert.Null(result.Metadata);
            Assert.Null(result.RawText);
            Assert.Contains("reparse", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                if (File.Exists(pubspecPath)) File.Delete(pubspecPath);
                if (Directory.Exists(outsideDirectory)) Directory.Delete(outsideDirectory, recursive: true);
            }
            catch
            {
                // Cleanup must not hide assertion results.
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
            // Cleanup must not hide assertion results.
        }
    }

    private PubspecParseResult Parse(string raw)
    {
        var pubspecPath = Path.Combine(_root, "pubspec.yaml");
        File.WriteAllText(pubspecPath, raw);
        return new PubspecParser().Parse(SuccessfulRoot(pubspecPath));
    }

    private FlutterProjectRootResult SuccessfulRoot(string pubspecPath)
        => new(
            FlutterProjectRootStatus.Succeeded,
            _root,
            _root,
            pubspecPath,
            Array.Empty<FlutterProjectCandidate>(),
            new[] { pubspecPath },
            "Test project root.");
}
