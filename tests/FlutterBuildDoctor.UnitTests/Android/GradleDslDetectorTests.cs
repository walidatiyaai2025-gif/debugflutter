using FlutterBuildDoctor.Android.ProjectAnalysis;

namespace FlutterBuildDoctor.UnitTests.Android;

public sealed class GradleDslDetectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fbd-gradle-dsl-" + Guid.NewGuid().ToString("N"));
    private readonly GradleDslDetector _detector = new();

    public GradleDslDetectorTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Detect_GroovyFlutterLayout_ReturnsAllKnownScriptRoles()
    {
        Write("android/settings.gradle");
        Write("android/build.gradle");
        Write("android/app/build.gradle");

        var result = _detector.Detect(_root);

        Assert.True(result.IsSuccess);
        Assert.Equal(GradleDslDetectionStatus.Succeeded, result.Status);
        Assert.Equal(GradleDslKind.Groovy, result.EffectiveDsl);
        Assert.Equal(3, result.Scripts.Count);
        Assert.Equal(GradleDslKind.Groovy, Assert.IsType<GradleScriptEvidence>(result.SettingsScript).Dsl);
        Assert.Equal(GradleDslKind.Groovy, Assert.IsType<GradleScriptEvidence>(result.ProjectBuildScript).Dsl);
        Assert.Equal(GradleDslKind.Groovy, Assert.IsType<GradleScriptEvidence>(result.AppBuildScript).Dsl);
        Assert.All(result.Scripts, script => Assert.True(Path.IsPathFullyQualified(script.Path)));
    }

    [Fact]
    public void Detect_KotlinFlutterLayout_ReturnsKotlinDsl()
    {
        Write("android/settings.gradle.kts");
        Write("android/build.gradle.kts");
        Write("android/app/build.gradle.kts");

        var result = _detector.Detect(_root);

        Assert.True(result.IsSuccess);
        Assert.Equal(GradleDslDetectionStatus.Succeeded, result.Status);
        Assert.Equal(GradleDslKind.Kotlin, result.EffectiveDsl);
        Assert.All(result.Scripts, script => Assert.Equal(GradleDslKind.Kotlin, script.Dsl));
    }

    [Fact]
    public void Detect_MixedLayout_PreservesPerScriptDslAndReturnsMixedStatus()
    {
        Write("android/settings.gradle.kts");
        Write("android/build.gradle");
        Write("android/app/build.gradle.kts");

        var result = _detector.Detect(_root);

        Assert.True(result.IsSuccess);
        Assert.Equal(GradleDslDetectionStatus.MixedDsl, result.Status);
        Assert.Equal(GradleDslKind.Mixed, result.EffectiveDsl);
        Assert.Equal(GradleDslKind.Kotlin, result.SettingsScript!.Dsl);
        Assert.Equal(GradleDslKind.Groovy, result.ProjectBuildScript!.Dsl);
        Assert.Equal(GradleDslKind.Kotlin, result.AppBuildScript!.Dsl);
    }

    [Fact]
    public void Detect_BothDslFilesForSameRole_ReturnsConflictAndPreservesBothPaths()
    {
        Write("android/settings.gradle");
        Write("android/settings.gradle.kts");
        Write("android/app/build.gradle.kts");

        var result = _detector.Detect(_root);

        Assert.False(result.IsSuccess);
        Assert.Equal(GradleDslDetectionStatus.ConflictingScripts, result.Status);
        Assert.Equal(GradleDslKind.Mixed, result.EffectiveDsl);
        Assert.Equal(2, result.Scripts.Count(script => script.Role == GradleScriptRole.Settings));
        Assert.Contains("Settings", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Detect_SettingsOnly_ReturnsBuildScriptsNotFoundButKeepsSettingsEvidence()
    {
        Write("android/settings.gradle.kts");

        var result = _detector.Detect(_root);

        Assert.Equal(GradleDslDetectionStatus.BuildScriptsNotFound, result.Status);
        Assert.Single(result.Scripts);
        Assert.Equal(GradleScriptRole.Settings, result.Scripts[0].Role);
        Assert.Equal(GradleDslKind.Unknown, result.EffectiveDsl);
    }

    [Fact]
    public void Detect_ProjectBuildOnly_IsStillValidStaticLayoutEvidence()
    {
        Write("android/build.gradle");

        var result = _detector.Detect(_root);

        Assert.True(result.IsSuccess);
        Assert.Equal(GradleDslKind.Groovy, result.EffectiveDsl);
        Assert.NotNull(result.ProjectBuildScript);
        Assert.Null(result.AppBuildScript);
    }

    [Fact]
    public void Detect_NoAndroidDirectory_ReturnsExplicitStatus()
    {
        var result = _detector.Detect(_root);

        Assert.Equal(GradleDslDetectionStatus.AndroidDirectoryNotFound, result.Status);
        Assert.Empty(result.Scripts);
    }

    [Fact]
    public void Detect_MissingProjectRoot_ReturnsExplicitStatus()
    {
        var missing = Path.Combine(_root, "missing");

        var result = _detector.Detect(missing);

        Assert.Equal(GradleDslDetectionStatus.ProjectRootNotFound, result.Status);
        Assert.Empty(result.Scripts);
    }

    [Fact]
    public void Detect_EmptyProjectRoot_ReturnsInvalidRequest()
    {
        var result = _detector.Detect("   ");

        Assert.Equal(GradleDslDetectionStatus.InvalidRequest, result.Status);
        Assert.Empty(result.Scripts);
    }

    [Fact]
    public void Detect_AndroidDirectorySymlink_IsRejectedWhenPlatformSupportsIt()
    {
        var outside = Path.Combine(Path.GetTempPath(), "fbd-gradle-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "build.gradle"), "// outside\n");
        var android = Path.Combine(_root, "android");

        try
        {
            try
            {
                Directory.CreateSymbolicLink(android, outside);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
            {
                return;
            }

            if ((File.GetAttributes(android) & FileAttributes.ReparsePoint) == 0)
                return;

            var result = _detector.Detect(_root);

            Assert.Equal(GradleDslDetectionStatus.InspectionFailed, result.Status);
            Assert.Empty(result.Scripts);
            Assert.Contains("reparse", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                if (Directory.Exists(android)) Directory.Delete(android);
                if (Directory.Exists(outside)) Directory.Delete(outside, recursive: true);
            }
            catch
            {
                // Cleanup must not hide assertion failures.
            }
        }
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

    private void Write(string relativePath)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "// fixture\n");
    }
}
