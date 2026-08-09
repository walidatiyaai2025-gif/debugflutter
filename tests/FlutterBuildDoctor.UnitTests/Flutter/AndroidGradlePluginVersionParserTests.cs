using FlutterBuildDoctor.Flutter.ProjectAnalysis;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class AndroidGradlePluginVersionParserTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fbd-agp-version-" + Guid.NewGuid().ToString("N"));
    private readonly AndroidGradlePluginVersionParser _parser = new();

    public AndroidGradlePluginVersionParserTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Parse_ModernGroovyPluginDeclaration_ReturnsVersion()
    {
        var dsl = GradleDsl((GradleScriptRole.Settings, GradleDslKind.Groovy, "settings.gradle",
            "plugins { id 'com.android.application' version '8.7.3' apply false }"));

        var result = _parser.Parse(dsl);

        Assert.True(result.IsSuccess);
        Assert.Equal("8.7.3", result.Version);
        var evidence = Assert.Single(result.Evidence);
        Assert.Equal(AndroidGradlePluginDeclarationKind.ModernPluginDsl, evidence.DeclarationKind);
        Assert.Equal("com.android.application", evidence.PluginId);
    }

    [Fact]
    public void Parse_ModernKotlinPluginDeclaration_ReturnsVersion()
    {
        var dsl = GradleDsl((GradleScriptRole.Settings, GradleDslKind.Kotlin, "settings.gradle.kts",
            "plugins { id(\"com.android.library\") version \"8.8.1\" apply false }"));

        var result = _parser.Parse(dsl);

        Assert.True(result.IsSuccess);
        Assert.Equal("8.8.1", result.Version);
        Assert.Equal(GradleDslKind.Kotlin, result.GradleDsl.EffectiveDsl);
    }

    [Fact]
    public void Parse_LegacyBuildscriptClasspath_ReturnsVersion()
    {
        var dsl = GradleDsl((GradleScriptRole.ProjectBuild, GradleDslKind.Groovy, "build.gradle",
            "buildscript { dependencies { classpath 'com.android.tools.build:gradle:7.4.2' } }"));

        var result = _parser.Parse(dsl);

        Assert.True(result.IsSuccess);
        Assert.Equal("7.4.2", result.Version);
        Assert.Equal(AndroidGradlePluginDeclarationKind.LegacyBuildscriptClasspath, Assert.Single(result.Evidence).DeclarationKind);
    }

    [Fact]
    public void Parse_CommentedOutVersions_AreIgnored()
    {
        var dsl = GradleDsl((GradleScriptRole.Settings, GradleDslKind.Kotlin, "settings.gradle.kts",
            "// id(\"com.android.application\") version \"9.9.9\"\n/* id(\"com.android.library\") version \"9.8.7\" */\nplugins { id(\"com.android.application\") version \"8.9.0\" apply false }"));

        var result = _parser.Parse(dsl);

        Assert.True(result.IsSuccess);
        Assert.Equal("8.9.0", result.Version);
        Assert.Single(result.Evidence);
    }

    [Fact]
    public void Parse_SameVersionAcrossModernAndLegacyDeclarations_SucceedsWithAllEvidence()
    {
        var dsl = GradleDsl(
            (GradleScriptRole.Settings, GradleDslKind.Groovy, "settings.gradle",
                "plugins { id 'com.android.application' version '8.6.1' apply false }"),
            (GradleScriptRole.ProjectBuild, GradleDslKind.Groovy, "build.gradle",
                "buildscript { dependencies { classpath 'com.android.tools.build:gradle:8.6.1' } }"));

        var result = _parser.Parse(dsl);

        Assert.True(result.IsSuccess);
        Assert.Equal("8.6.1", result.Version);
        Assert.Equal(2, result.Evidence.Count);
    }

    [Fact]
    public void Parse_ConflictingExplicitVersions_ReturnsAmbiguous()
    {
        var dsl = GradleDsl(
            (GradleScriptRole.Settings, GradleDslKind.Kotlin, "settings.gradle.kts",
                "plugins { id(\"com.android.application\") version \"8.7.0\" apply false }"),
            (GradleScriptRole.ProjectBuild, GradleDslKind.Kotlin, "build.gradle.kts",
                "buildscript { dependencies { classpath(\"com.android.tools.build:gradle:8.6.0\") } }"));

        var result = _parser.Parse(dsl);

        Assert.Equal(AndroidGradlePluginVersionStatus.Ambiguous, result.Status);
        Assert.Null(result.Version);
        Assert.Equal(2, result.Evidence.Count);
    }

    [Fact]
    public void Parse_DynamicOrCatalogStyleVersion_DoesNotGuess()
    {
        var dsl = GradleDsl((GradleScriptRole.Settings, GradleDslKind.Kotlin, "settings.gradle.kts",
            "plugins { id(\"com.android.application\") version libs.versions.agp.get() apply false }"));

        var result = _parser.Parse(dsl);

        Assert.Equal(AndroidGradlePluginVersionStatus.VersionNotFound, result.Status);
        Assert.Null(result.Version);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public void Parse_UnsuccessfulGradleDsl_ReturnsUnavailable()
    {
        var root = SuccessfulRoot();
        var dsl = new GradleDslDetectionResult(
            GradleDslDetectionStatus.Ambiguous,
            root,
            Path.Combine(_root, "android"),
            GradleDslKind.Mixed,
            Array.Empty<GradleScriptEvidence>(),
            "Ambiguous scripts.");

        var result = _parser.Parse(dsl);

        Assert.Equal(AndroidGradlePluginVersionStatus.GradleDslUnavailable, result.Status);
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
            // Cleanup must not hide assertion failures.
        }
    }

    private GradleDslDetectionResult GradleDsl(params (GradleScriptRole Role, GradleDslKind Dsl, string FileName, string Content)[] scripts)
    {
        var evidence = new List<GradleScriptEvidence>();
        foreach (var script in scripts)
        {
            var path = Path.Combine(_root, "android", script.FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, script.Content);
            evidence.Add(new GradleScriptEvidence(script.Role, script.Dsl, path));
        }

        var distinct = scripts.Select(script => script.Dsl).Distinct().ToArray();
        return new GradleDslDetectionResult(
            GradleDslDetectionStatus.Succeeded,
            SuccessfulRoot(),
            Path.Combine(_root, "android"),
            distinct.Length == 1 ? distinct[0] : GradleDslKind.Mixed,
            evidence,
            "Test Gradle DSL.");
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
