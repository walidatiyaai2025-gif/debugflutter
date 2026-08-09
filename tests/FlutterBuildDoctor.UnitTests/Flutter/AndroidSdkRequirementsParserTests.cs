using FlutterBuildDoctor.Flutter.ProjectAnalysis;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class AndroidSdkRequirementsParserTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fbd-sdk-req-" + Guid.NewGuid().ToString("N"));

    public AndroidSdkRequirementsParserTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Parse_GroovyStaticValues_ReturnsAllApiLevels()
    {
        var dsl = AppDsl(
            GradleDslKind.Groovy,
            """
            android {
              compileSdkVersion 35
              defaultConfig {
                minSdkVersion 23
                targetSdkVersion 35
              }
            }
            """);

        var result = new AndroidSdkRequirementsParser().Parse(dsl);

        Assert.Equal(AndroidSdkRequirementsStatus.Succeeded, result.Status);
        Assert.Equal(35, result.CompileSdk?.ApiLevel);
        Assert.Equal(23, result.MinSdk?.ApiLevel);
        Assert.Equal(35, result.TargetSdk?.ApiLevel);
        Assert.All(new[] { result.CompileSdk!, result.MinSdk!, result.TargetSdk! }, value =>
            Assert.Equal(AndroidSdkLevelValueKind.StaticApiLevel, value.Kind));
    }

    [Fact]
    public void Parse_KotlinStaticValues_ReturnsAllApiLevels()
    {
        var dsl = AppDsl(
            GradleDslKind.Kotlin,
            """
            android {
              compileSdk = 36
              defaultConfig {
                minSdk = 24
                targetSdk = 36
              }
            }
            """);

        var result = new AndroidSdkRequirementsParser().Parse(dsl);

        Assert.True(result.IsSuccess);
        Assert.Equal(36, result.CompileSdk?.ApiLevel);
        Assert.Equal(24, result.MinSdk?.ApiLevel);
        Assert.Equal(36, result.TargetSdk?.ApiLevel);
    }

    [Fact]
    public void Parse_CommonFlutterReferences_ReturnsTypedReferences()
    {
        var dsl = AppDsl(
            GradleDslKind.Groovy,
            """
            android {
              compileSdkVersion flutter.compileSdkVersion
              defaultConfig {
                minSdkVersion flutter.minSdkVersion
                targetSdkVersion flutter.targetSdkVersion
              }
            }
            """);

        var result = new AndroidSdkRequirementsParser().Parse(dsl);

        Assert.Equal(AndroidSdkRequirementsStatus.Succeeded, result.Status);
        Assert.Equal("flutter.compileSdkVersion", result.CompileSdk?.FlutterReference);
        Assert.Equal("flutter.minSdkVersion", result.MinSdk?.FlutterReference);
        Assert.Equal("flutter.targetSdkVersion", result.TargetSdk?.FlutterReference);
        Assert.All(new[] { result.CompileSdk!, result.MinSdk!, result.TargetSdk! }, value =>
            Assert.Equal(AndroidSdkLevelValueKind.FlutterReference, value.Kind));
    }

    [Fact]
    public void Parse_MixedStaticAndFlutterReferences_IsSupported()
    {
        var dsl = AppDsl(
            GradleDslKind.Kotlin,
            """
            android {
              compileSdk = flutter.compileSdkVersion
              defaultConfig {
                minSdk = 24
                targetSdk = flutter.targetSdkVersion
              }
            }
            """);

        var result = new AndroidSdkRequirementsParser().Parse(dsl);

        Assert.Equal(AndroidSdkRequirementsStatus.Succeeded, result.Status);
        Assert.Equal(AndroidSdkLevelValueKind.FlutterReference, result.CompileSdk?.Kind);
        Assert.Equal(24, result.MinSdk?.ApiLevel);
        Assert.Equal(AndroidSdkLevelValueKind.FlutterReference, result.TargetSdk?.Kind);
    }

    [Fact]
    public void Parse_DynamicExpression_IsReportedUnresolvedWithoutGuessing()
    {
        var dsl = AppDsl(
            GradleDslKind.Kotlin,
            """
            android {
              compileSdk = libs.versions.compileSdk.get().toInt()
              defaultConfig {
                minSdk = 24
                targetSdk = targetApi
              }
            }
            """);

        var result = new AndroidSdkRequirementsParser().Parse(dsl);

        Assert.Equal(AndroidSdkRequirementsStatus.Partial, result.Status);
        Assert.Null(result.CompileSdk);
        Assert.Equal(24, result.MinSdk?.ApiLevel);
        Assert.Null(result.TargetSdk);
        Assert.Contains(AndroidSdkLevelField.CompileSdk, result.UnresolvedFields);
        Assert.Contains(AndroidSdkLevelField.TargetSdk, result.UnresolvedFields);
    }

    [Fact]
    public void Parse_OnlyDynamicExpressions_ReturnsRequirementsNotFound()
    {
        var dsl = AppDsl(
            GradleDslKind.Groovy,
            """
            android {
              compileSdkVersion rootProject.ext.compileSdk
              defaultConfig {
                minSdkVersion projectMinSdk
                targetSdkVersion targetApi
              }
            }
            """);

        var result = new AndroidSdkRequirementsParser().Parse(dsl);

        Assert.Equal(AndroidSdkRequirementsStatus.RequirementsNotFound, result.Status);
        Assert.Equal(3, result.UnresolvedFields.Count);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public void Parse_ConflictingDeclarations_ReturnsAmbiguous()
    {
        var dsl = AppDsl(
            GradleDslKind.Groovy,
            """
            android {
              compileSdkVersion 34
              compileSdkVersion 35
              defaultConfig {
                minSdkVersion 23
                targetSdkVersion 35
              }
            }
            """);

        var result = new AndroidSdkRequirementsParser().Parse(dsl);

        Assert.Equal(AndroidSdkRequirementsStatus.Ambiguous, result.Status);
        Assert.Null(result.CompileSdk);
        Assert.Equal(23, result.MinSdk?.ApiLevel);
        Assert.Equal(35, result.TargetSdk?.ApiLevel);
    }

    [Fact]
    public void Parse_CommentedAndStringMentions_AreIgnored()
    {
        var dsl = AppDsl(
            GradleDslKind.Groovy,
            """
            // compileSdkVersion 99
            def note = "minSdkVersion 88 targetSdkVersion 88"
            android {
              compileSdkVersion 35
              defaultConfig {
                minSdkVersion 23
                targetSdkVersion 35
              }
            }
            """);

        var result = new AndroidSdkRequirementsParser().Parse(dsl);

        Assert.Equal(AndroidSdkRequirementsStatus.Succeeded, result.Status);
        Assert.Equal(35, result.CompileSdk?.ApiLevel);
        Assert.Equal(23, result.MinSdk?.ApiLevel);
        Assert.Equal(35, result.TargetSdk?.ApiLevel);
    }

    [Fact]
    public void Parse_MissingAppScriptEvidence_DoesNotInspectOtherRoles()
    {
        var android = Path.Combine(_root, "android");
        Directory.CreateDirectory(android);
        var projectScript = Path.Combine(android, "build.gradle");
        File.WriteAllText(projectScript, "compileSdkVersion 35");
        var dsl = Dsl(new GradleScriptEvidence(GradleScriptRole.ProjectBuild, GradleDslKind.Groovy, projectScript));

        var result = new AndroidSdkRequirementsParser().Parse(dsl);

        Assert.Equal(AndroidSdkRequirementsStatus.AppBuildScriptUnavailable, result.Status);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public void ResultContract_DoesNotExposeRawGradleScriptText()
    {
        Assert.DoesNotContain(
            typeof(AndroidSdkRequirementsResult).GetProperties(),
            property => property.Name.Contains("Raw", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("Content", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { }
    }

    private GradleDslDetectionResult AppDsl(GradleDslKind dsl, string content)
    {
        var android = Path.Combine(_root, "android");
        var app = Path.Combine(android, "app");
        Directory.CreateDirectory(app);
        var path = Path.Combine(app, dsl == GradleDslKind.Kotlin ? "build.gradle.kts" : "build.gradle");
        File.WriteAllText(path, content);
        return Dsl(new GradleScriptEvidence(GradleScriptRole.AppBuild, dsl, path));
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
