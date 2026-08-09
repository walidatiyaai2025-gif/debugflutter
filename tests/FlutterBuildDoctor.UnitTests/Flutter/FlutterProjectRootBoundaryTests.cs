using FlutterBuildDoctor.Flutter.ProjectAnalysis;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class FlutterProjectRootBoundaryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fbd-project-root-boundary-" + Guid.NewGuid().ToString("N"));

    public FlutterProjectRootBoundaryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Locate_SdkFlutterTextAlone_DoesNotPromotePubspecWithoutFilesystemEvidence()
    {
        File.WriteAllText(
            Path.Combine(_root, "pubspec.yaml"),
            "name: claimed_flutter\ndependencies:\n  flutter:\n    sdk: flutter\n");

        var result = new FlutterProjectRootLocator().Locate(_root);

        Assert.Equal(FlutterProjectRootStatus.NotFlutterProject, result.Status);
        Assert.Null(result.EffectiveRoot);
        Assert.Empty(result.Candidates);
        Assert.Single(result.InspectedPubspecPaths);
        Assert.Contains("intentionally not parsed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Locate_MetadataEvidence_DoesNotRequireYamlParsing()
    {
        File.WriteAllText(Path.Combine(_root, "pubspec.yaml"), "not: [valid: yaml");
        File.WriteAllText(Path.Combine(_root, ".metadata"), "version:\n  revision: test\n");

        var result = new FlutterProjectRootLocator().Locate(_root);

        Assert.True(result.IsSuccess);
        Assert.Equal(Path.GetFullPath(_root), result.EffectiveRoot);
        Assert.True(Assert.Single(result.Candidates).HasMetadataFile);
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
            // Cleanup must not hide an assertion result.
        }
    }
}
