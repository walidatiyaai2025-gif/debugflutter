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
        WriteFlutterPubspec(_root);
        WriteFlutterPubspec(Path.Combine(_root, "example"));

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
        WriteFlutterPubspec(appRoot);

        var result = _locator.Locate(_root);

        Assert.True(result.IsSuccess);
        Assert.Equal(Path.GetFullPath(appRoot), result.EffectiveRoot);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public void Locate_WhenMultipleNestedFlutterProjectsExist_ReturnsAmbiguous()
    {
        WriteFlutterPubspec(Path.Combine(_root, "apps", "one"));
        WriteFlutterPubspec(Path.Combine(_root, "apps", "two"));

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
    public void Locate_WhenPubspecIsNotFlutter_ReturnsNotFlutterProject()
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
    public void Locate_WhenMetadataExists_ValidatesFlutterProjectWithoutParsingFullPubspec()
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
        WriteFlutterPubspec(Path.Combine(_root, "build", "generated-app"));
        WriteFlutterPubspec(Path.Combine(_root, ".dart_tool", "cached-app"));

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
    public void Locate_DetectsQuotedFlutterSdkDependency()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, "pubspec.yaml"),
            "name: quoted\ndependencies:\n  flutter:\n    sdk: 'flutter'\n");

        var result = _locator.Locate(_root);

        Assert.True(result.IsSuccess);
        Assert.True(Assert.Single(result.Candidates).HasFlutterSdkDependency);
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

    private static void WriteFlutterPubspec(string directory)
    {
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, "lib"));
        Directory.CreateDirectory(Path.Combine(directory, "android"));
        File.WriteAllText(
            Path.Combine(directory, "pubspec.yaml"),
            "name: sample\ndependencies:\n  flutter:\n    sdk: flutter\n");
    }
}
