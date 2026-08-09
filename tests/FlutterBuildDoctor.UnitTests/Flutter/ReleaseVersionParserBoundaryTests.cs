using FlutterBuildDoctor.Flutter.ProjectAnalysis;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class ReleaseVersionParserBoundaryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "fbd-release-version-boundary-" + Guid.NewGuid().ToString("N"));

    public ReleaseVersionParserBoundaryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Parse_InjectedAppScriptOutsideExpectedLocation_IsRejected()
    {
        var android = Path.Combine(_root, "android");
        Directory.CreateDirectory(Path.Combine(android, "app"));
        var outside = Path.Combine(_root, "outside.gradle");
        File.WriteAllText(outside, "android { defaultConfig { versionName '9.0.0'; versionCode 9 } }");

        var result = new ReleaseVersionParser().Parse(
            Pubspec(),
            Dsl(new GradleScriptEvidence(GradleScriptRole.AppBuild, GradleDslKind.Groovy, outside)));

        Assert.Equal(ReleaseVersionStatus.UnsafePath, result.Status);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public void Parse_StaleAppScriptEvidence_IsRejected()
    {
        var android = Path.Combine(_root, "android");
        var app = Path.Combine(android, "app");
        Directory.CreateDirectory(app);
        var path = Path.Combine(app, "build.gradle");

        var result = new ReleaseVersionParser().Parse(
            Pubspec(),
            Dsl(new GradleScriptEvidence(GradleScriptRole.AppBuild, GradleDslKind.Groovy, path)));

        Assert.Equal(ReleaseVersionStatus.AppBuildScriptUnavailable, result.Status);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public void Parse_AppScriptSymlink_IsRejectedWhenSupported()
    {
        var android = Path.Combine(_root, "android");
        var app = Path.Combine(android, "app");
        Directory.CreateDirectory(app);
        var outside = Path.Combine(
            Path.GetTempPath(),
            "fbd-release-version-outside-" + Guid.NewGuid().ToString("N") + ".gradle");
        File.WriteAllText(outside, "android { defaultConfig { versionName '9.0.0'; versionCode 9 } }");
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

            var result = new ReleaseVersionParser().Parse(
                Pubspec(),
                Dsl(new GradleScriptEvidence(GradleScriptRole.AppBuild, GradleDslKind.Groovy, link)));

            Assert.Equal(ReleaseVersionStatus.UnsafePath, result.Status);
            Assert.Empty(result.Evidence);
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
    public void Parse_OversizeAppScript_IsRejectedBeforeParsing()
    {
        var android = Path.Combine(_root, "android");
        var app = Path.Combine(android, "app");
        Directory.CreateDirectory(app);
        var path = Path.Combine(app, "build.gradle");
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            stream.SetLength((512L * 1024) + 1);

        var result = new ReleaseVersionParser().Parse(
            Pubspec(),
            Dsl(new GradleScriptEvidence(GradleScriptRole.AppBuild, GradleDslKind.Groovy, path)));

        Assert.Equal(ReleaseVersionStatus.FileTooLarge, result.Status);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public void Parse_MissingAppBuildRole_DoesNotInspectProjectBuildScript()
    {
        var android = Path.Combine(_root, "android");
        Directory.CreateDirectory(android);
        var path = Path.Combine(android, "build.gradle");
        File.WriteAllText(path, "android { defaultConfig { versionName '9.0.0'; versionCode 9 } }");

        var result = new ReleaseVersionParser().Parse(
            Pubspec(),
            Dsl(new GradleScriptEvidence(GradleScriptRole.ProjectBuild, GradleDslKind.Groovy, path)));

        Assert.Equal(ReleaseVersionStatus.AppBuildScriptUnavailable, result.Status);
        Assert.Empty(result.Evidence);
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

    private PubspecParseResult Pubspec()
    {
        var path = Path.Combine(_root, "pubspec.yaml");
        var metadata = new PubspecMetadata(
            "sample_app",
            null,
            "1.0.0+1",
            null,
            null,
            null,
            null,
            null,
            ">=3.0.0 <4.0.0",
            null,
            Array.Empty<string>(),
            Array.Empty<PubspecDependency>());

        return new PubspecParseResult(
            PubspecParseStatus.Succeeded,
            SuccessfulRoot(),
            path,
            metadata,
            null,
            "Test pubspec.");
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
