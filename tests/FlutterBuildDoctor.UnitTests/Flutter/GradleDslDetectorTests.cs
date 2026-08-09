using FlutterBuildDoctor.Flutter.ProjectAnalysis;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class GradleDslDetectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fbd-gradle-dsl-" + Guid.NewGuid().ToString("N"));
    private readonly GradleDslDetector _detector = new();

    public GradleDslDetectorTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Detect_GroovyLayout_ReportsGroovyAndPaths()
    {
        WriteAndroidScript("settings.gradle");
        WriteAndroidScript("build.gradle");
        WriteAndroidScript(Path.Combine("app", "build.gradle"));

        var result = _detector.Detect(SuccessfulRoot());

        Assert.True(result.IsSuccess);
        Assert.Equal(GradleDslKind.Groovy, result.EffectiveDsl);
        Assert.Equal(3, result.Scripts.Count);
        Assert.EndsWith("settings.gradle", Assert.IsType<GradleScriptEvidence>(result.SettingsScript).Path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("build.gradle", Assert.IsType<GradleScriptEvidence>(result.ProjectBuildScript).Path, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Path.Combine("app", "build.gradle"), Assert.IsType<GradleScriptEvidence>(result.AppBuildScript).Path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Detect_KotlinLayout_ReportsKotlin()
    {
        WriteAndroidScript("settings.gradle.kts");
        WriteAndroidScript("build.gradle.kts");
        WriteAndroidScript(Path.Combine("app", "build.gradle.kts"));

        var result = _detector.Detect(SuccessfulRoot());

        Assert.True(result.IsSuccess);
        Assert.Equal(GradleDslKind.Kotlin, result.EffectiveDsl);
        Assert.All(result.Scripts, script => Assert.Equal(GradleDslKind.Kotlin, script.Dsl));
    }

    [Fact]
    public void Detect_MixedRoles_ReportsMixedWithoutGuessingOneGlobalDsl()
    {
        WriteAndroidScript("settings.gradle.kts");
        WriteAndroidScript("build.gradle");
        WriteAndroidScript(Path.Combine("app", "build.gradle.kts"));

        var result = _detector.Detect(SuccessfulRoot());

        Assert.True(result.IsSuccess);
        Assert.Equal(GradleDslKind.Mixed, result.EffectiveDsl);
        Assert.Equal(3, result.Scripts.Count);
    }

    [Fact]
    public void Detect_BothVariantsForSameRole_ReturnsAmbiguousAndPreservesEvidence()
    {
        WriteAndroidScript("build.gradle");
        WriteAndroidScript("build.gradle.kts");
        WriteAndroidScript(Path.Combine("app", "build.gradle"));

        var result = _detector.Detect(SuccessfulRoot());

        Assert.Equal(GradleDslDetectionStatus.Ambiguous, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Equal(GradleDslKind.Mixed, result.EffectiveDsl);
        Assert.Equal(3, result.Scripts.Count);
        Assert.Contains("ProjectBuild", result.Message, StringComparison.Ordinal);
        Assert.NotNull(result.ProjectBuildScript);
    }

    [Fact]
    public void Detect_NoAndroidDirectory_ReturnsAndroidDirectoryMissing()
    {
        var result = _detector.Detect(SuccessfulRoot());

        Assert.Equal(GradleDslDetectionStatus.AndroidDirectoryMissing, result.Status);
        Assert.Empty(result.Scripts);
    }

    [Fact]
    public void Detect_AndroidDirectoryWithoutBuildScripts_ReturnsBuildScriptsMissing()
    {
        Directory.CreateDirectory(Path.Combine(_root, "android"));
        WriteAndroidScript("settings.gradle");

        var result = _detector.Detect(SuccessfulRoot());

        Assert.Equal(GradleDslDetectionStatus.BuildScriptsMissing, result.Status);
        Assert.Single(result.Scripts);
        Assert.Equal(GradleScriptRole.Settings, result.Scripts[0].Role);
    }

    [Fact]
    public void Detect_OnlyAppBuildScript_SucceedsAndReportsMissingEvidence()
    {
        WriteAndroidScript(Path.Combine("app", "build.gradle.kts"));

        var result = _detector.Detect(SuccessfulRoot());

        Assert.True(result.IsSuccess);
        Assert.Equal(GradleDslKind.Kotlin, result.EffectiveDsl);
        Assert.Contains("project build script", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("settings script", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Detect_UnsuccessfulProjectRoot_ReturnsProjectRootUnavailable()
    {
        var root = new FlutterProjectRootResult(
            FlutterProjectRootStatus.Ambiguous,
            _root,
            null,
            null,
            Array.Empty<FlutterProjectCandidate>(),
            Array.Empty<string>(),
            "Ambiguous project root.");

        var result = _detector.Detect(root);

        Assert.Equal(GradleDslDetectionStatus.ProjectRootUnavailable, result.Status);
        Assert.Null(result.AndroidDirectory);
        Assert.Empty(result.Scripts);
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

    private void WriteAndroidScript(string relativePath)
    {
        var path = Path.Combine(_root, "android", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "// fixture only");
    }

    private FlutterProjectRootResult SuccessfulRoot()
        => new(
            FlutterProjectRootStatus.Succeeded,
            _root,
            _root,
            Path.Combine(_root, "pubspec.yaml"),
            Array.Empty<FlutterProjectCandidate>(),
            new[] { Path.Combine(_root, "pubspec.yaml") },
            "Test project root.");
}
