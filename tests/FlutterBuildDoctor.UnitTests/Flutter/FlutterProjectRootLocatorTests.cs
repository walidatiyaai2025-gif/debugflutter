using FlutterBuildDoctor.Flutter.ProjectAnalysis;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class FlutterProjectRootLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fbd-project-root-" + Guid.NewGuid().ToString("N"));
    private readonly FlutterProjectRootLocator _locator = new();

    public FlutterProjectRootLocatorTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Locate_WhenRepositoryRootIsFlutterProject_PrefersRootAndRetainsNestedEvidence()
    {
        WriteFlutterProject(_root);
        WriteFlutterProject(Path.Combine(_root, "example"));

        var result = _locator.Locate(_root);

        Assert.True(result.IsSuccess);
        Assert.Equal(FlutterProjectRootStatus.Succeeded, result.Status);
        Assert.Equal(Path.GetFullPath(_root), result.EffectiveRoot);
        Assert.Equal(Path.Combine(Path.GetFullPath(_root), "pubspec.yaml"), result.EffectivePubspecPath);
        Assert.Equal(2, result.Candidates.Count);
        Assert.Contains("repository root", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Locate_WhenExactlyOneNestedFlutterProjectExists_SelectsIt()
    {
        File.WriteAllText(Path.Combine(_root, "README.md"), "monorepo");
        var appRoot = Path.Combine(_root, "apps", "mobile");
        WriteFlutterProject(appRoot);

        var result = _locator.Locate(_root);

        Assert.True(result.IsSuccess);
        Assert.Equal(Path.GetFullPath(appRoot), result.EffectiveRoot);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public void Locate_WhenMultipleNestedFlutterProjectsExist_ReturnsAmbiguous()
    {
        WriteFlutterProject(Path.Combine(_root, "apps", "one"));
        WriteFlutterProject(Path.Combine(_root, "apps", "two"));

        var result = _locator.Locate(_root);

        Assert.Equal(FlutterProjectRootStatus.Ambiguous, result.Status);
        Assert.Null(result.EffectiveRoot);
        Assert.Equal(2, result.Candidates.Count);
    }

    [Fact]
    public void Locate_WhenNoPubspecExists_ReturnsPubspecNotFound()
    {
        Directory.CreateDirectory(Path.Combine(_root, "lib"));

        var result = _locator.Locate(_root);

        Assert.Equal(FlutterProjectRootStatus.PubspecNotFound, result.Status);
        Assert.Empty(result.InspectedPubspecPaths);
    }

    [Fact]
    public void Locate_WhenPubspecHasNoFlutterFilesystemEvidence_ReturnsNotFlutterProject()
    {
        File.WriteAllText(
            Path.Combine(_root, "pubspec.yaml"),
            "name: plain_dart\ndependencies:\n  collection: ^1.19.0\n");

        var result = _locator.Locate(_root);

        Assert.Equal(FlutterProjectRootStatus.NotFlutterProject, result.Status);
        Assert.Single(result.InspectedPubspecPaths);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void Locate_WhenMetadataExists_ValidatesFlutterProjectWithoutParsingPubspec()
    {
        File.WriteAllText(Path.Combine(_root, "pubspec.yaml"), "name: metadata_project\n");
        File.WriteAllText(Path.Combine(_root, ".metadata"), "version:\n  revision: test\n");

        var result = _locator.Locate(_root);

        Assert.True(result.IsSuccess);
        Assert.True(Assert.Single(result.Candidates).HasMetadataFile);
    }

    [Fact]
    public void Locate_IgnoresGeneratedBuildAndDartToolTrees()
    {
        WriteFlutterProject(Path.Combine(_root, "build", "generated-app"));
        WriteFlutterProject(Path.Combine(_root, ".dart_tool", "cached-app"));

        var result = _locator.Locate(_root);

        Assert.Equal(FlutterProjectRootStatus.PubspecNotFound, result.Status);
        Assert.Empty(result.InspectedPubspecPaths);
    }

    [Fact]
    public void Locate_WhenPathDoesNotExist_ReturnsRepositoryNotFound()
    {
        var missing = Path.Combine(_root, "missing");

        var result = _locator.Locate(missing);

        Assert.Equal(FlutterProjectRootStatus.RepositoryNotFound, result.Status);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Locate_UsesFilesystemEvidenceWithoutParsingPubspecContents()
    {
        Directory.CreateDirectory(Path.Combine(_root, "lib"));
        Directory.CreateDirectory(Path.Combine(_root, "android"));
        File.WriteAllText(
            Path.Combine(_root, "pubspec.yaml"),
            "name: filesystem_evidence_only\ndependencies:\n  collection: ^1.19.0\n");

        var result = _locator.Locate(_root);

        Assert.True(result.IsSuccess);
        var candidate = Assert.Single(result.Candidates);
        Assert.True(candidate.HasLibDirectory);
        Assert.True(candidate.HasAndroidDirectory);
        Assert.True(candidate.HasFlutterProjectEvidence);
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
            // Test cleanup must not hide the assertion result.
        }
    }

    private static void WriteFlutterProject(string directory)
    {
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, "lib"));
        Directory.CreateDirectory(Path.Combine(directory, "android"));
        File.WriteAllText(
            Path.Combine(directory, "pubspec.yaml"),
            "name: sample\ndependencies:\n  flutter:\n    sdk: flutter\n");
    }
}
