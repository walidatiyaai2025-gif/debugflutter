using FlutterBuildDoctor.Flutter.ProjectAnalysis;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class AndroidIdentifierParserTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fbd-android-id-" + Guid.NewGuid().ToString("N"));

    public AndroidIdentifierParserTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Parse_GroovyStaticIdentifiers_ReturnsNamespaceAndApplicationId()
    {
        var result = Parse(
            """
            android {
              namespace 'com.example.shell'
              defaultConfig {
                applicationId "com.example.app"
              }
            }
            """);

        Assert.Equal(AndroidIdentifierStatus.Succeeded, result.Status);
        Assert.Equal("com.example.shell", result.Namespace?.Value);
        Assert.Equal("com.example.app", result.ApplicationId?.Value);
        Assert.Empty(result.UnresolvedFields);
    }

    [Fact]
    public void Parse_KotlinAssignments_ReturnsNamespaceAndApplicationId()
    {
        var result = Parse(
            """
            android {
              namespace = "com.example.kotlin"
              defaultConfig {
                applicationId = "com.example.kotlin.app"
              }
            }
            """,
            GradleDslKind.Kotlin);

        Assert.True(result.IsSuccess);
        Assert.Equal("com.example.kotlin", result.Namespace?.Value);
        Assert.Equal("com.example.kotlin.app", result.ApplicationId?.Value);
    }

    [Fact]
    public void Parse_ParenthesizedLiteralCalls_AreSupported()
    {
        var result = Parse(
            """
            android {
              namespace("com.example.call")
              defaultConfig {
                applicationId("com.example.call.app")
              }
            }
            """,
            GradleDslKind.Kotlin);

        Assert.Equal(AndroidIdentifierStatus.Succeeded, result.Status);
        Assert.Equal("com.example.call", result.Namespace?.Value);
        Assert.Equal("com.example.call.app", result.ApplicationId?.Value);
    }

    [Fact]
    public void Parse_NamespaceAndApplicationIdMayDiffer_WithoutAmbiguity()
    {
        var result = Parse(
            """
            android {
              namespace = "com.example.librarynamespace"
              defaultConfig {
                applicationId = "com.example.shippedapp"
              }
            }
            """,
            GradleDslKind.Kotlin);

        Assert.Equal(AndroidIdentifierStatus.Succeeded, result.Status);
        Assert.NotEqual(result.Namespace?.Value, result.ApplicationId?.Value);
    }

    [Fact]
    public void Parse_DynamicApplicationId_ReturnsPartialWithoutGuessing()
    {
        var result = Parse(
            """
            android {
              namespace = "com.example.static"
              defaultConfig {
                applicationId = project.findProperty("appId") as String
              }
            }
            """,
            GradleDslKind.Kotlin);

        Assert.Equal(AndroidIdentifierStatus.Partial, result.Status);
        Assert.Equal("com.example.static", result.Namespace?.Value);
        Assert.Null(result.ApplicationId);
        Assert.Contains(AndroidIdentifierField.ApplicationId, result.UnresolvedFields);
    }

    [Fact]
    public void Parse_InterpolatedLiteral_IsUnresolvedInsteadOfTruncated()
    {
        var result = Parse(
            """
            android {
              namespace = "com.example.${suffix}"
              defaultConfig {
                applicationId = "com.example.app"
              }
            }
            """,
            GradleDslKind.Kotlin);

        Assert.Equal(AndroidIdentifierStatus.Partial, result.Status);
        Assert.Null(result.Namespace);
        Assert.Equal("com.example.app", result.ApplicationId?.Value);
        Assert.Contains(AndroidIdentifierField.Namespace, result.UnresolvedFields);
    }

    [Fact]
    public void Parse_ConcatenatedLiteral_IsUnresolvedInsteadOfAcceptingPrefix()
    {
        var result = Parse(
            """
            android {
              namespace = "com.example." + namespaceSuffix
              defaultConfig {
                applicationId = "com.example.app"
              }
            }
            """,
            GradleDslKind.Kotlin);

        Assert.Equal(AndroidIdentifierStatus.Partial, result.Status);
        Assert.Null(result.Namespace);
        Assert.Equal("com.example.app", result.ApplicationId?.Value);
    }

    [Fact]
    public void Parse_ConflictingNamespaceDeclarations_ReturnsAmbiguous()
    {
        var result = Parse(
            """
            android {
              namespace "com.example.one"
              namespace "com.example.two"
              defaultConfig {
                applicationId "com.example.app"
              }
            }
            """);

        Assert.Equal(AndroidIdentifierStatus.Ambiguous, result.Status);
        Assert.Null(result.Namespace);
        Assert.Equal("com.example.app", result.ApplicationId?.Value);
    }

    [Fact]
    public void Parse_RepeatedIdenticalDeclarations_AreNotAmbiguous()
    {
        var result = Parse(
            """
            android {
              namespace "com.example.same"
              namespace "com.example.same"
              defaultConfig {
                applicationId "com.example.app"
                applicationId "com.example.app"
              }
            }
            """);

        Assert.Equal(AndroidIdentifierStatus.Succeeded, result.Status);
        Assert.Equal("com.example.same", result.Namespace?.Value);
        Assert.Equal("com.example.app", result.ApplicationId?.Value);
    }

    [Fact]
    public void Parse_CommentsStringsAndSuffixes_AreIgnored()
    {
        var result = Parse(
            """
            // namespace "com.fake.comment"
            def note = "applicationId 'com.fake.string'"
            android {
              namespace "com.example.real"
              defaultConfig {
                applicationId "com.example.real.app"
                applicationIdSuffix ".debug"
              }
            }
            """);

        Assert.Equal(AndroidIdentifierStatus.Succeeded, result.Status);
        Assert.Equal("com.example.real", result.Namespace?.Value);
        Assert.Equal("com.example.real.app", result.ApplicationId?.Value);
        Assert.DoesNotContain(result.Evidence, item => item.Value.Contains("fake", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_IdentifiersOutsideExpectedDslScopes_AreIgnored()
    {
        var result = Parse(
            """
            namespace "com.fake.top"
            applicationId "com.fake.top.app"
            android {
              defaultConfig {
                namespace "com.fake.nested"
                applicationId "com.example.real.app"
              }
              buildTypes {
                release {
                  applicationId "com.fake.release"
                }
              }
            }
            """);

        Assert.Equal(AndroidIdentifierStatus.Partial, result.Status);
        Assert.Null(result.Namespace);
        Assert.Equal("com.example.real.app", result.ApplicationId?.Value);
        Assert.DoesNotContain(result.Evidence, item => item.Value.Contains("fake", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_MissingAppScriptEvidence_DoesNotInspectOtherRoles()
    {
        var android = Path.Combine(_root, "android");
        Directory.CreateDirectory(android);
        var projectScript = Path.Combine(android, "build.gradle");
        File.WriteAllText(projectScript, "android { namespace 'com.fake.project' }");
        var result = new AndroidIdentifierParser().Parse(
            Dsl(new GradleScriptEvidence(GradleScriptRole.ProjectBuild, GradleDslKind.Groovy, projectScript)));

        Assert.Equal(AndroidIdentifierStatus.AppBuildScriptUnavailable, result.Status);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public void ResultContract_DoesNotExposeRawGradleSource()
    {
        Assert.DoesNotContain(
            typeof(AndroidIdentifierResult).GetProperties(),
            property => property.Name.Contains("Raw", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("Content", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { }
    }

    private AndroidIdentifierResult Parse(string content, GradleDslKind dsl = GradleDslKind.Groovy)
    {
        var android = Path.Combine(_root, "android");
        var app = Path.Combine(android, "app");
        Directory.CreateDirectory(app);
        var path = Path.Combine(app, dsl == GradleDslKind.Kotlin ? "build.gradle.kts" : "build.gradle");
        File.WriteAllText(path, content);
        return new AndroidIdentifierParser().Parse(
            Dsl(new GradleScriptEvidence(GradleScriptRole.AppBuild, dsl, path)));
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
