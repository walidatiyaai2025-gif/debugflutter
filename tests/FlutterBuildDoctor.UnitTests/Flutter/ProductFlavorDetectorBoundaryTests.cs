using FlutterBuildDoctor.Flutter.ProjectAnalysis;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class ProductFlavorDetectorBoundaryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "fbd-product-flavor-boundary-" + Guid.NewGuid().ToString("N"));

    public ProductFlavorDetectorBoundaryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Detect_InjectedAppScriptOutsideExpectedLocation_IsRejected()
    {
        var android = Path.Combine(_root, "android");
        Directory.CreateDirectory(Path.Combine(android, "app"));
        var outside = Path.Combine(_root, "outside.gradle");
        File.WriteAllText(outside, "android { productFlavors { demo { } } }");

        var result = new ProductFlavorDetector().Detect(
            Dsl(new GradleScriptEvidence(GradleScriptRole.AppBuild, GradleDslKind.Groovy, outside)));

        Assert.Equal(ProductFlavorDetectionStatus.UnsafePath, result.Status);
        Assert.Empty(result.Flavors);
    }

    [Fact]
    public void Detect_StaleAppBuildScriptEvidence_IsRejected()
    {
        var android = Path.Combine(_root, "android");
        var app = Path.Combine(android, "app");
        Directory.CreateDirectory(app);
        var path = Path.Combine(app, "build.gradle");

        var result = new ProductFlavorDetector().Detect(
            Dsl(new GradleScriptEvidence(GradleScriptRole.AppBuild, GradleDslKind.Groovy, path)));

        Assert.Equal(ProductFlavorDetectionStatus.AppBuildScriptUnavailable, result.Status);
        Assert.Empty(result.Flavors);
    }

    [Fact]
    public void Detect_AppBuildScriptSymlink_IsRejectedWhenSupported()
    {
        var android = Path.Combine(_root, "android");
        var app = Path.Combine(android, "app");
        Directory.CreateDirectory(app);
        var outside = Path.Combine(
            Path.GetTempPath(),
            "fbd-product-flavor-outside-" + Guid.NewGuid().ToString("N") + ".gradle");
        File.WriteAllText(outside, "android { productFlavors { demo { } } }");
        var link = Path.Combine(app, "build.gradle");

        try
        {
            try
            {
                File.CreateSymbolicLink(link, outside);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
            {
                return;
            }

            if ((File.GetAttributes(link) & FileAttributes.ReparsePoint) == 0)
                return;

            var result = new ProductFlavorDetector().Detect(
                Dsl(new GradleScriptEvidence(GradleScriptRole.AppBuild, GradleDslKind.Groovy, link)));

            Assert.Equal(ProductFlavorDetectionStatus.UnsafePath, result.Status);
            Assert.Empty(result.Flavors);
        }
        finally
        {
            try
            {
                if (File.Exists(link)) File.Delete(link);
                if (File.Exists(outside)) File.Delete(outside);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void Detect_OversizeAppBuildScript_IsRejectedBeforeParsing()
    {
        var android = Path.Combine(_root, "android");
        var app = Path.Combine(android, "app");
        Directory.CreateDirectory(app);
        var path = Path.Combine(app, "build.gradle");
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            stream.SetLength((512L * 1024) + 1);

        var result = new ProductFlavorDetector().Detect(
            Dsl(new GradleScriptEvidence(GradleScriptRole.AppBuild, GradleDslKind.Groovy, path)));

        Assert.Equal(ProductFlavorDetectionStatus.FileTooLarge, result.Status);
        Assert.Empty(result.Flavors);
    }

    [Fact]
    public void Detect_MissingAppBuildRole_DoesNotInspectProjectBuildScript()
    {
        var android = Path.Combine(_root, "android");
        Directory.CreateDirectory(android);
        var path = Path.Combine(android, "build.gradle");
        File.WriteAllText(path, "android { productFlavors { demo { } } }");

        var result = new ProductFlavorDetector().Detect(
            Dsl(new GradleScriptEvidence(GradleScriptRole.ProjectBuild, GradleDslKind.Groovy, path)));

        Assert.Equal(ProductFlavorDetectionStatus.AppBuildScriptUnavailable, result.Status);
        Assert.Empty(result.Flavors);
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
        }
    }

    private GradleDslDetectionResult Dsl(params GradleScriptEvidence[] scripts)
        => new(
            GradleDslDetectionStatus.Succeeded,
            SuccessfulRoot(),
            Path.Combine(_root, "android"),
            scripts.Select(script => script.Dsl).Distinct().Count() == 1 ? scripts[0].Dsl : GradleDslKind.Mixed,
            scripts,
            "Test Gradle DSL.");

    private FlutterProjectRootResult SuccessfulRoot()
        => new(
            FlutterProjectRootStatus.Succeeded,
            _root,
            _root,
            Path.Combine(_root, "pubspec.yaml"),
            Array.Empty<FlutterProjectCandidate>(),
            new[] { Path.Combine(_root, "pubspec.yaml") },
            "Test root.");
}
