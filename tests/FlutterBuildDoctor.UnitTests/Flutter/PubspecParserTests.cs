using FlutterBuildDoctor.Flutter.ProjectAnalysis;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class PubspecParserTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fbd-pubspec-" + Guid.NewGuid().ToString("N"));
    private readonly PubspecParser _parser = new();

    public PubspecParserTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Parse_ValidPubspec_ReadsIdentitySdkConstraintsAndDependencySources()
    {
        var pubspecPath = WritePubspec(
            """
            name: sample_app
            description: Sample Flutter application
            version: 1.2.3+4
            publish_to: none
            repository: https://github.com/example/sample_app
            environment:
              sdk: ">=3.5.0 <4.0.0"
              flutter: ">=3.24.0"
            topics:
              - flutter
              - tooling
            dependencies:
              flutter:
                sdk: flutter
              http: ^1.2.0
              local_package:
                path: ../local_package
              git_package:
                git:
                  url: https://github.com/example/git_package.git
                  ref: main
                  path: packages/core
            dev_dependencies:
              flutter_test:
                sdk: flutter
            dependency_overrides:
              http: 1.2.1
            """);

        var result = _parser.Parse(SuccessfulRoot(pubspecPath));

        Assert.True(result.IsSuccess);
        Assert.Equal(PubspecParseStatus.Succeeded, result.Status);
        Assert.Equal(pubspecPath, result.PubspecPath);
        Assert.NotNull(result.RawText);
        var metadata = Assert.IsType<PubspecMetadata>(result.Metadata);
        Assert.Equal("sample_app", metadata.Name);
        Assert.Equal("1.2.3+4", metadata.Version);
        Assert.Equal(">=3.5.0 <4.0.0", metadata.DartSdkConstraint);
        Assert.Equal(">=3.24.0", metadata.FlutterSdkConstraint);
        Assert.Equal(new[] { "flutter", "tooling" }, metadata.Topics);
        Assert.True(metadata.HasFlutterSdkDependency);

        var flutter = Assert.Single(metadata.Dependencies, dependency =>
            dependency.Name == "flutter" && dependency.Section == PubspecDependencySection.Dependencies);
        Assert.Equal(PubspecDependencyKind.Sdk, flutter.Kind);
        Assert.Equal("flutter", flutter.Sdk);

        var hosted = Assert.Single(metadata.Dependencies, dependency =>
            dependency.Name == "http" && dependency.Section == PubspecDependencySection.Dependencies);
        Assert.Equal(PubspecDependencyKind.Hosted, hosted.Kind);
        Assert.Equal("^1.2.0", hosted.Constraint);

        var local = Assert.Single(metadata.Dependencies, dependency => dependency.Name == "local_package");
        Assert.Equal(PubspecDependencyKind.Path, local.Kind);
        Assert.Equal("../local_package", local.Path);

        var git = Assert.Single(metadata.Dependencies, dependency => dependency.Name == "git_package");
        Assert.Equal(PubspecDependencyKind.Git, git.Kind);
        Assert.Equal("https://github.com/example/git_package.git", git.GitUrl?.TrimEnd('/'));
        Assert.Equal("main", git.GitRef);
        Assert.Equal("packages/core", git.GitPath);

        Assert.Contains(metadata.Dependencies, dependency =>
            dependency.Name == "flutter_test" && dependency.Section == PubspecDependencySection.DevDependencies);
        Assert.Contains(metadata.Dependencies, dependency =>
            dependency.Name == "http" && dependency.Section == PubspecDependencySection.DependencyOverrides && dependency.Constraint == "1.2.1");
    }

    [Fact]
    public void Parse_MalformedYaml_ReturnsExplicitFailureAndPreservesSourceText()
    {
        var pubspecPath = WritePubspec("name: sample\ndependencies:\n  broken: [one, two\n");

        var result = _parser.Parse(SuccessfulRoot(pubspecPath));

        Assert.Equal(PubspecParseStatus.MalformedYaml, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Metadata);
        Assert.NotNull(result.RawText);
        Assert.Contains("broken", result.RawText, StringComparison.Ordinal);
        Assert.Contains("malformed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_MissingName_ReturnsMissingRequiredField()
    {
        var pubspecPath = WritePubspec("environment:\n  sdk: ^3.5.0\n");

        var result = _parser.Parse(SuccessfulRoot(pubspecPath));

        Assert.Equal(PubspecParseStatus.MissingRequiredField, result.Status);
        Assert.Null(result.Metadata);
        Assert.NotNull(result.RawText);
    }

    [Fact]
    public void Parse_UnsuccessfulProjectRoot_DoesNotInspectFilesystem()
    {
        var rootResult = new FlutterProjectRootResult(
            FlutterProjectRootStatus.Ambiguous,
            _root,
            null,
            null,
            Array.Empty<FlutterProjectCandidate>(),
            Array.Empty<string>(),
            "Ambiguous test root.");

        var result = _parser.Parse(rootResult);

        Assert.Equal(PubspecParseStatus.ProjectRootUnavailable, result.Status);
        Assert.Null(result.PubspecPath);
        Assert.Null(result.RawText);
    }

    [Fact]
    public void Parse_WhenResolvedPubspecWasRemoved_ReturnsPubspecNotFound()
    {
        var pubspecPath = Path.Combine(_root, "pubspec.yaml");

        var result = _parser.Parse(SuccessfulRoot(pubspecPath));

        Assert.Equal(PubspecParseStatus.PubspecNotFound, result.Status);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public void Parse_RejectsPubspecOutsideEffectiveRoot()
    {
        var nested = Path.Combine(_root, "nested");
        Directory.CreateDirectory(nested);
        var unexpectedPubspec = Path.Combine(nested, "pubspec.yaml");
        File.WriteAllText(unexpectedPubspec, "name: nested\n");

        var result = _parser.Parse(SuccessfulRoot(unexpectedPubspec));

        Assert.Equal(PubspecParseStatus.InvalidRequest, result.Status);
        Assert.Null(result.Metadata);
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

    private string WritePubspec(string content)
    {
        var path = Path.Combine(_root, "pubspec.yaml");
        File.WriteAllText(path, content);
        return path;
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
