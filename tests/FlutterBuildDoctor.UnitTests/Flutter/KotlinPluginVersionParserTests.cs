using FlutterBuildDoctor.Flutter.ProjectAnalysis;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class KotlinPluginVersionParserTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fbd-kotlin-plugin-" + Guid.NewGuid().ToString("N"));

    public KotlinPluginVersionParserTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Parse_ModernGroovyPluginDsl_ReturnsVersion()
    {
        var script = Script(
            GradleScriptRole.Settings,
            GradleDslKind.Groovy,
            "settings.gradle",
            "plugins { id 'org.jetbrains.kotlin.android' version '2.0.21' apply false }");

        var result = new KotlinPluginVersionParser().Parse(Dsl(script));

        Assert.True(result.IsSuccess);
        Assert.Equal("2.0.21", result.Version);
        var item = Assert.Single(result.Evidence);
        Assert.Equal(KotlinPluginDeclarationKind.ModernPluginDsl, item.DeclarationKind);
        Assert.Equal("org.jetbrains.kotlin.android", item.PluginId);
    }

    [Fact]
    public void Parse_ModernKotlinPluginDsl_ReturnsVersion()
    {
        var script = Script(
            GradleScriptRole.Settings,
            GradleDslKind.Kotlin,
            "settings.gradle.kts",
            "plugins { id(\"org.jetbrains.kotlin.android\") version \"2.1.10\" apply false }");

        var result = new KotlinPluginVersionParser().Parse(Dsl(script));

        Assert.True(result.IsSuccess);
        Assert.Equal("2.1.10", result.Version);
    }

    [Fact]
    public void Parse_KotlinDslShorthand_ReturnsVersion()
    {
        var script = Script(
            GradleScriptRole.Settings,
            GradleDslKind.Kotlin,
            "settings.gradle.kts",
            "plugins { kotlin(\"android\") version \"2.0.21\" apply false }");

        var result = new KotlinPluginVersionParser().Parse(Dsl(script));

        Assert.True(result.IsSuccess);
        Assert.Equal("2.0.21", result.Version);
        Assert.Equal(KotlinPluginDeclarationKind.KotlinDslShorthand, Assert.Single(result.Evidence).DeclarationKind);
    }

    [Fact]
    public void Parse_LegacyLiteralClasspath_ReturnsVersion()
    {
        var script = Script(
            GradleScriptRole.ProjectBuild,
            GradleDslKind.Groovy,
            "build.gradle",
            "buildscript { dependencies { classpath 'org.jetbrains.kotlin:kotlin-gradle-plugin:1.9.24' } }");

        var result = new KotlinPluginVersionParser().Parse(Dsl(script));

        Assert.True(result.IsSuccess);
        Assert.Equal("1.9.24", result.Version);
        Assert.Equal(KotlinPluginDeclarationKind.LegacyBuildscriptClasspath, Assert.Single(result.Evidence).DeclarationKind);
    }

    [Fact]
    public void Parse_LegacyKotlinVersionPropertyWithPluginContext_ReturnsVersion()
    {
        var script = Script(
            GradleScriptRole.ProjectBuild,
            GradleDslKind.Groovy,
            "build.gradle",
            """
            buildscript {
              ext.kotlin_version = '1.9.24'
              dependencies {
                classpath "org.jetbrains.kotlin:kotlin-gradle-plugin:$kotlin_version"
              }
            }
            """);

        var result = new KotlinPluginVersionParser().Parse(Dsl(script));

        Assert.True(result.IsSuccess);
        Assert.Equal("1.9.24", result.Version);
        Assert.Equal(KotlinPluginDeclarationKind.LegacyVersionProperty, Assert.Single(result.Evidence).DeclarationKind);
    }

    [Fact]
    public void Parse_KotlinVersionPropertyWithoutPluginContext_IsNotTreatedAsPluginVersion()
    {
        var script = Script(
            GradleScriptRole.ProjectBuild,
            GradleDslKind.Groovy,
            "build.gradle",
            "ext.kotlin_version = '1.9.24'");

        var result = new KotlinPluginVersionParser().Parse(Dsl(script));

        Assert.Equal(KotlinPluginVersionStatus.VersionNotFound, result.Status);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public void Parse_CommentedDeclarations_AreIgnored()
    {
        var script = Script(
            GradleScriptRole.Settings,
            GradleDslKind.Groovy,
            "settings.gradle",
            "// id 'org.jetbrains.kotlin.android' version '9.9.9'\n/* classpath 'org.jetbrains.kotlin:kotlin-gradle-plugin:8.8.8' */");

        var result = new KotlinPluginVersionParser().Parse(Dsl(script));

        Assert.Equal(KotlinPluginVersionStatus.VersionNotFound, result.Status);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public void Parse_DynamicPluginVersion_IsNotGuessed()
    {
        var script = Script(
            GradleScriptRole.Settings,
            GradleDslKind.Groovy,
            "settings.gradle",
            "plugins { id 'org.jetbrains.kotlin.android' version kotlinVersion apply false }");

        var result = new KotlinPluginVersionParser().Parse(Dsl(script));

        Assert.Equal(KotlinPluginVersionStatus.VersionNotFound, result.Status);
        Assert.Null(result.Version);
        Assert.Contains("not guessed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_RepeatedSameVersion_SucceedsAndPreservesEvidence()
    {
        var settings = Script(
            GradleScriptRole.Settings,
            GradleDslKind.Groovy,
            "settings.gradle",
            "plugins { id 'org.jetbrains.kotlin.android' version '2.0.21' apply false }");
        var build = Script(
            GradleScriptRole.ProjectBuild,
            GradleDslKind.Groovy,
            "build.gradle",
            "buildscript { dependencies { classpath 'org.jetbrains.kotlin:kotlin-gradle-plugin:2.0.21' } }");

        var result = new KotlinPluginVersionParser().Parse(Dsl(settings, build));

        Assert.True(result.IsSuccess);
        Assert.Equal("2.0.21", result.Version);
        Assert.Equal(2, result.Evidence.Count);
    }

    [Fact]
    public void Parse_ConflictingVersions_ReturnsAmbiguous()
    {
        var settings = Script(
            GradleScriptRole.Settings,
            GradleDslKind.Groovy,
            "settings.gradle",
            "plugins { id 'org.jetbrains.kotlin.android' version '2.0.21' apply false }");
        var build = Script(
            GradleScriptRole.ProjectBuild,
            GradleDslKind.Groovy,
            "build.gradle",
            "buildscript { dependencies { classpath 'org.jetbrains.kotlin:kotlin-gradle-plugin:1.9.24' } }");

        var result = new KotlinPluginVersionParser().Parse(Dsl(settings, build));

        Assert.Equal(KotlinPluginVersionStatus.Ambiguous, result.Status);
        Assert.Null(result.Version);
        Assert.Equal(2, result.Evidence.Count);
    }

    [Fact]
    public void ResultContract_DoesNotExposeRawGradleScriptText()
    {
        Assert.DoesNotContain(
            typeof(KotlinPluginVersionResult).GetProperties(),
            property => property.Name.Contains("Raw", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("Content", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { }
    }

    private GradleScriptEvidence Script(GradleScriptRole role, GradleDslKind dsl, string relativePath, string content)
    {
        var android = Path.Combine(_root, "android");
        var path = Path.Combine(android, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return new GradleScriptEvidence(role, dsl, path);
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
