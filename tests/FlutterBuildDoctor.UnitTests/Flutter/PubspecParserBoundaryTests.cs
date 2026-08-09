using FlutterBuildDoctor.Flutter.ProjectAnalysis;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class PubspecParserBoundaryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fbd-pubspec-boundary-" + Guid.NewGuid().ToString("N"));
    private readonly PubspecParser _parser = new();

    public PubspecParserBoundaryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Parse_HostedMappingAndGitScalar_ArePreservedAsTypedDependencies()
    {
        var pubspecPath = WritePubspec(
            """
            name: dependency_shapes
            dependencies:
              git_scalar:
                git: https://github.com/example/git_scalar.git
              private_hosted:
                hosted:
                  name: private_hosted
                  url: https://packages.example.com/api
                version: ^2.0.0
              unknown_source:
                custom_source: custom-value
                version: ^3.0.0
            """);

        var result = _parser.Parse(SuccessfulRoot(pubspecPath));

        Assert.True(result.IsSuccess);
        var metadata = Assert.IsType<PubspecMetadata>(result.Metadata);

        var git = Assert.Single(metadata.Dependencies, dependency => dependency.Name == "git_scalar");
        Assert.Equal(PubspecDependencyKind.Git, git.Kind);
        Assert.Equal("https://github.com/example/git_scalar.git", git.GitUrl?.TrimEnd('/'));
        Assert.Null(git.GitRef);
        Assert.Null(git.GitPath);

        var hosted = Assert.Single(metadata.Dependencies, dependency => dependency.Name == "private_hosted");
        Assert.Equal(PubspecDependencyKind.Hosted, hosted.Kind);
        Assert.Equal("private_hosted", hosted.HostedName);
        Assert.Equal("https://packages.example.com/api", hosted.HostedUrl?.TrimEnd('/'));
        Assert.Equal("^2.0.0", hosted.Constraint);

        var unknown = Assert.Single(metadata.Dependencies, dependency => dependency.Name == "unknown_source");
        Assert.Equal(PubspecDependencyKind.Unknown, unknown.Kind);
        Assert.Equal("^3.0.0", unknown.Constraint);
    }

    [Fact]
    public void Parse_MultipleYamlDocuments_ReturnsInvalidDocumentAndPreservesRawText()
    {
        const string raw = "name: first\n---\nname: second\n";
        var pubspecPath = WritePubspec(raw);

        var result = _parser.Parse(SuccessfulRoot(pubspecPath));

        Assert.Equal(PubspecParseStatus.InvalidDocument, result.Status);
        Assert.Equal(raw, result.RawText);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public void Parse_ScalarDocument_ReturnsInvalidDocument()
    {
        const string raw = "plain_scalar\n";
        var pubspecPath = WritePubspec(raw);

        var result = _parser.Parse(SuccessfulRoot(pubspecPath));

        Assert.Equal(PubspecParseStatus.InvalidDocument, result.Status);
        Assert.Equal(raw, result.RawText);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public void Parse_OversizedPubspec_ReturnsFileTooLargeWithoutReadingRawText()
    {
        var pubspecPath = Path.Combine(_root, "pubspec.yaml");
        using (var stream = new FileStream(pubspecPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.SetLength((2L * 1024 * 1024) + 1);
        }

        var result = _parser.Parse(SuccessfulRoot(pubspecPath));

        Assert.Equal(PubspecParseStatus.FileTooLarge, result.Status);
        Assert.Null(result.RawText);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public void Parse_DoesNotReadOrModifyPubspecLock()
    {
        var pubspecPath = WritePubspec("name: lock_boundary\n");
        var lockPath = Path.Combine(_root, "pubspec.lock");
        const string lockText = "not: valid: lock: syntax\nPRIVATE_LOCK_MARKER";
        File.WriteAllText(lockPath, lockText);
        var originalBytes = File.ReadAllBytes(lockPath);
        var originalWriteTime = File.GetLastWriteTimeUtc(lockPath);

        var result = _parser.Parse(SuccessfulRoot(pubspecPath));

        Assert.True(result.IsSuccess);
        Assert.Equal("lock_boundary", result.Metadata!.Name);
        Assert.Equal(originalBytes, File.ReadAllBytes(lockPath));
        Assert.Equal(originalWriteTime, File.GetLastWriteTimeUtc(lockPath));
        Assert.DoesNotContain("PRIVATE_LOCK_MARKER", result.RawText, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_EffectivePubspecMustBeDirectChildOfEffectiveRoot()
    {
        var nested = Path.Combine(_root, "nested");
        Directory.CreateDirectory(nested);
        var nestedPubspec = Path.Combine(nested, "pubspec.yaml");
        File.WriteAllText(nestedPubspec, "name: nested\n");

        var root = new FlutterProjectRootResult(
            FlutterProjectRootStatus.Succeeded,
            _root,
            _root,
            nestedPubspec,
            Array.Empty<FlutterProjectCandidate>(),
            new[] { nestedPubspec },
            "Fabricated root evidence.");

        var result = _parser.Parse(root);

        Assert.Equal(PubspecParseStatus.InvalidRequest, result.Status);
        Assert.Null(result.RawText);
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
