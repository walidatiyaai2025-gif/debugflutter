using FlutterBuildDoctor.Flutter.ProjectAnalysis;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class AndroidSdkRequirementsParserScopeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fbd-sdk-scope-" + Guid.NewGuid().ToString("N"));

    public AndroidSdkRequirementsParserScopeTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Parse_TopLevelHelperVariables_AreNotAcceptedAsSdkRequirements()
    {
        var result = Parse(
            """
            def compileSdkVersion = 99
            def minSdkVersion = 88
            def targetSdkVersion = 77

            android {
              compileSdkVersion 35
              defaultConfig {
                minSdkVersion 23
                targetSdkVersion 35
              }
            }
            """);

        Assert.Equal(AndroidSdkRequirementsStatus.Succeeded, result.Status);
        Assert.Equal(35, result.CompileSdk?.ApiLevel);
        Assert.Equal(23, result.MinSdk?.ApiLevel);
        Assert.Equal(35, result.TargetSdk?.ApiLevel);
        Assert.DoesNotContain(result.Evidence, evidence => evidence.ApiLevel is 99 or 88 or 77);
    }

    [Fact]
    public void Parse_MinAndTargetOutsideDefaultConfig_AreNotAccepted()
    {
        var result = Parse(
            """
            android {
              compileSdk = 35
              minSdk = 10
              targetSdk = 11
              defaultConfig {
                minSdk = 24
                targetSdk = 35
              }
            }
            """,
            GradleDslKind.Kotlin);

        Assert.Equal(AndroidSdkRequirementsStatus.Succeeded, result.Status);
        Assert.Equal(35, result.CompileSdk?.ApiLevel);
        Assert.Equal(24, result.MinSdk?.ApiLevel);
        Assert.Equal(35, result.TargetSdk?.ApiLevel);
        Assert.DoesNotContain(result.Evidence, evidence => evidence.ApiLevel is 10 or 11);
    }

    [Fact]
    public void Parse_NoAndroidBlock_DoesNotUseLookalikeDeclarations()
    {
        var result = Parse(
            """
            compileSdkVersion 35
            defaultConfig {
              minSdkVersion 23
              targetSdkVersion 35
            }
            """);

        Assert.Equal(AndroidSdkRequirementsStatus.RequirementsNotFound, result.Status);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public void Parse_UnreasonableNumericValue_IsLeftUnresolvedInsteadOfAccepted()
    {
        var result = Parse(
            """
            android {
              compileSdk = 9999
              defaultConfig {
                minSdk = 24
                targetSdk = 35
              }
            }
            """,
            GradleDslKind.Kotlin);

        Assert.Equal(AndroidSdkRequirementsStatus.Partial, result.Status);
        Assert.Null(result.CompileSdk);
        Assert.Contains(AndroidSdkLevelField.CompileSdk, result.UnresolvedFields);
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

    private AndroidSdkRequirementsResult Parse(string content, GradleDslKind dsl = GradleDslKind.Groovy)
    {
        var android = Path.Combine(_root, "android");
        var app = Path.Combine(android, "app");
        Directory.CreateDirectory(app);
        var scriptPath = Path.Combine(app, dsl == GradleDslKind.Kotlin ? "build.gradle.kts" : "build.gradle");
        File.WriteAllText(scriptPath, content);

        var root = new FlutterProjectRootResult(
            FlutterProjectRootStatus.Succeeded,
            _root,
            _root,
            Path.Combine(_root, "pubspec.yaml"),
            Array.Empty<FlutterProjectCandidate>(),
            new[] { Path.Combine(_root, "pubspec.yaml") },
            "Test root.");
        var gradle = new GradleDslDetectionResult(
            GradleDslDetectionStatus.Succeeded,
            root,
            android,
            dsl,
            new[] { new GradleScriptEvidence(GradleScriptRole.AppBuild, dsl, scriptPath) },
            "Test Gradle DSL.");

        return new AndroidSdkRequirementsParser().Parse(gradle);
    }
}
