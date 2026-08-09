using FlutterBuildDoctor.Flutter.ProjectAnalysis;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class AndroidSdkRequirementsParserBoundaryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fbd-sdk-req-boundary-" + Guid.NewGuid().ToString("N"));

    public AndroidSdkRequirementsParserBoundaryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Parse_InjectedAppScriptPathOutsideExpectedRole_IsRejected()
    {
        var android = Path.Combine(_root, "android");
        Directory.CreateDirectory(Path.Combine(android, "app"));
        var outside = Path.Combine(_root, "outside.gradle");
        File.WriteAllText(outside, "compileSdkVersion 35");
        var dsl = Dsl(new GradleScriptEvidence(GradleScriptRole.AppBuild, GradleDslKind.Groovy, outside));

        var result = new AndroidSdkRequirementsParser().Parse(dsl);

        Assert.Equal(AndroidSdkRequirementsStatus.UnsafePath, result.Status);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public void Parse_AppBuildScriptSymlink_IsRejectedWhenSupported()
    {
        var android = Path.Combine(_root, "android");
        var app = Path.Combine(android, "app");
        Directory.CreateDirectory(app);
        var outside = Path.Combine(Path.GetTempPath(), "fbd-sdk-outside-" + Guid.NewGuid().ToString("N") + ".gradle");
        File.WriteAllText(outside, "compileSdkVersion 35\nminSdkVersion 23\ntargetSdkVersion 35\n");
        var link = Path.Combine(app, "build.gradle");

        try
        {
            try { File.CreateSymbolicLink(link, outside); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException) { return; }

            if ((File.GetAttributes(link) & FileAttributes.ReparsePoint) == 0)
                return;

            var result = new AndroidSdkRequirementsParser().Parse(
                Dsl(new GradleScriptEvidence(GradleScriptRole.AppBuild, GradleDslKind.Groovy, link)));

            Assert.Equal(AndroidSdkRequirementsStatus.UnsafePath, result.Status);
            Assert.Empty(result.Evidence);
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
    public void Parse_OversizeAppBuildScript_IsRejectedBeforeParsing()
    {
        var android = Path.Combine(_root, "android");
        var app = Path.Combine(android, "app");
        Directory.CreateDirectory(app);
        var path = Path.Combine(app, "build.gradle");
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            stream.SetLength((512L * 1024) + 1);

        var result = new AndroidSdkRequirementsParser().Parse(
            Dsl(new GradleScriptEvidence(GradleScriptRole.AppBuild, GradleDslKind.Groovy, path)));

        Assert.Equal(AndroidSdkRequirementsStatus.FileTooLarge, result.Status);
        Assert.Empty(result.Evidence);
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
