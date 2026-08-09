using FlutterBuildDoctor.Flutter.ProjectAnalysis;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class AndroidGradlePluginVersionParserBoundaryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fbd-agp-boundary-" + Guid.NewGuid().ToString("N"));

    public AndroidGradlePluginVersionParserBoundaryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Parse_InjectedScriptPathOutsideExpectedRole_IsRejected()
    {
        var android = Path.Combine(_root, "android");
        Directory.CreateDirectory(android);
        var outside = Path.Combine(_root, "outside.gradle");
        File.WriteAllText(outside, "plugins { id 'com.android.application' version '8.7.3' }");
        var dsl = Dsl(new GradleScriptEvidence(GradleScriptRole.ProjectBuild, GradleDslKind.Groovy, outside));

        var result = new AndroidGradlePluginVersionParser().Parse(dsl);

        Assert.Equal(AndroidGradlePluginVersionStatus.UnsafePath, result.Status);
        Assert.Null(result.Version);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public void Parse_ScriptSymlink_IsRejectedWhenSupported()
    {
        var android = Path.Combine(_root, "android");
        Directory.CreateDirectory(android);
        var outside = Path.Combine(Path.GetTempPath(), "fbd-agp-outside-" + Guid.NewGuid().ToString("N") + ".gradle");
        File.WriteAllText(outside, "plugins { id 'com.android.application' version '8.7.3' }");
        var link = Path.Combine(android, "build.gradle");

        try
        {
            try { File.CreateSymbolicLink(link, outside); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException) { return; }

            if ((File.GetAttributes(link) & FileAttributes.ReparsePoint) == 0)
                return;

            var result = new AndroidGradlePluginVersionParser().Parse(
                Dsl(new GradleScriptEvidence(GradleScriptRole.ProjectBuild, GradleDslKind.Groovy, link)));

            Assert.Equal(AndroidGradlePluginVersionStatus.UnsafePath, result.Status);
            Assert.Null(result.Version);
        }
        finally
        {
            try
            {
                if (File.Exists(link)) File.Delete(link);
                if (File.Exists(outside)) File.Delete(outside);
            }
            catch { }
        }
    }

    [Fact]
    public void Parse_OversizeScript_IsRejectedBeforeContentParsing()
    {
        var android = Path.Combine(_root, "android");
        Directory.CreateDirectory(android);
        var script = Path.Combine(android, "build.gradle");
        using (var stream = new FileStream(script, FileMode.Create, FileAccess.Write, FileShare.None))
            stream.SetLength((512L * 1024) + 1);

        var result = new AndroidGradlePluginVersionParser().Parse(
            Dsl(new GradleScriptEvidence(GradleScriptRole.ProjectBuild, GradleDslKind.Groovy, script)));

        Assert.Equal(AndroidGradlePluginVersionStatus.FileTooLarge, result.Status);
        Assert.Null(result.Version);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { }
    }

    private GradleDslDetectionResult Dsl(params GradleScriptEvidence[] scripts)
        => new(
            GradleDslDetectionStatus.Succeeded,
            SuccessfulRoot(),
            Path.Combine(_root, "android"),
            scripts.Select(script => script.Dsl).Distinct().Count() == 1 ? scripts[0].Dsl : GradleDslKind.Mixed,
            scripts,
            "Test Gradle DSL.");

    private FlutterProjectRootResult SuccessfulRoot() => new(
        FlutterProjectRootStatus.Succeeded,
        _root,
        _root,
        Path.Combine(_root, "pubspec.yaml"),
        Array.Empty<FlutterProjectCandidate>(),
        new[] { Path.Combine(_root, "pubspec.yaml") },
        "Test root.");
}
