using FlutterBuildDoctor.Flutter.ProjectAnalysis;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class AndroidIdentifierParserConservatismTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "fbd-android-id-conservative-" + Guid.NewGuid().ToString("N"));

    public AndroidIdentifierParserConservatismTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Parse_GroovyHelperVariablesNamedLikeDslFields_AreIgnored()
    {
        var result = Parse(
            """
            android {
              def namespace = "com.fake.helper"
              namespace "com.example.real"
              defaultConfig {
                def applicationId = "com.fake.helper.app"
                applicationId "com.example.real.app"
              }
            }
            """);

        Assert.Equal(AndroidIdentifierStatus.Succeeded, result.Status);
        Assert.Equal("com.example.real", result.Namespace?.Value);
        Assert.Equal("com.example.real.app", result.ApplicationId?.Value);
        Assert.DoesNotContain(result.Evidence, item => item.Value.Contains("fake", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_StaticAndDynamicNamespaceDeclarations_DoNotSelectStaticAsEffectiveValue()
    {
        var result = Parse(
            """
            android {
              namespace "com.example.static"
              namespace project.findProperty("androidNamespace")
              defaultConfig {
                applicationId "com.example.app"
              }
            }
            """);

        Assert.Equal(AndroidIdentifierStatus.Partial, result.Status);
        Assert.Null(result.Namespace);
        Assert.Equal("com.example.app", result.ApplicationId?.Value);
        Assert.Contains(AndroidIdentifierField.Namespace, result.UnresolvedFields);
        Assert.Contains(
            result.Evidence,
            item => item.Field == AndroidIdentifierField.Namespace && item.Value == "com.example.static");
    }

    [Fact]
    public void Parse_StaticAndDynamicApplicationIdDeclarations_DoNotSelectStaticAsEffectiveValue()
    {
        var result = Parse(
            """
            android {
              namespace "com.example.namespace"
              defaultConfig {
                applicationId "com.example.static"
                applicationId project.findProperty("applicationId")
              }
            }
            """);

        Assert.Equal(AndroidIdentifierStatus.Partial, result.Status);
        Assert.Equal("com.example.namespace", result.Namespace?.Value);
        Assert.Null(result.ApplicationId);
        Assert.Contains(AndroidIdentifierField.ApplicationId, result.UnresolvedFields);
        Assert.Contains(
            result.Evidence,
            item => item.Field == AndroidIdentifierField.ApplicationId && item.Value == "com.example.static");
    }

    [Fact]
    public void Parse_OnlyMixedStaticDynamicDeclarations_ReturnsNoEffectiveValues()
    {
        var result = Parse(
            """
            android {
              namespace "com.example.static"
              namespace providers.gradleProperty("androidNamespace").get()
              defaultConfig {
                applicationId "com.example.app"
                applicationId providers.gradleProperty("applicationId").get()
              }
            }
            """);

        Assert.Equal(AndroidIdentifierStatus.IdentifiersNotFound, result.Status);
        Assert.Null(result.Namespace);
        Assert.Null(result.ApplicationId);
        Assert.Contains(AndroidIdentifierField.Namespace, result.UnresolvedFields);
        Assert.Contains(AndroidIdentifierField.ApplicationId, result.UnresolvedFields);
        Assert.NotEmpty(result.Evidence);
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

    private AndroidIdentifierResult Parse(string content)
    {
        var android = Path.Combine(_root, "android");
        var app = Path.Combine(android, "app");
        Directory.CreateDirectory(app);
        var path = Path.Combine(app, "build.gradle");
        File.WriteAllText(path, content);

        return new AndroidIdentifierParser().Parse(
            new GradleDslDetectionResult(
                GradleDslDetectionStatus.Succeeded,
                SuccessfulRoot(),
                android,
                GradleDslKind.Groovy,
                new[]
                {
                    new GradleScriptEvidence(
                        GradleScriptRole.AppBuild,
                        GradleDslKind.Groovy,
                        path)
                },
                "Test Gradle DSL."));
    }

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
