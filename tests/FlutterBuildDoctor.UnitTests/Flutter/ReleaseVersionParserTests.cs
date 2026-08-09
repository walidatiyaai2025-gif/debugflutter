using FlutterBuildDoctor.Flutter.ProjectAnalysis;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class ReleaseVersionParserTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "fbd-release-version-" + Guid.NewGuid().ToString("N"));

    public ReleaseVersionParserTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Parse_ModernKotlinFlutterReferences_ResolvesFromPubspec()
    {
        var result = Parse(
            """
            android {
              defaultConfig {
                versionCode = flutter.versionCode
                versionName = flutter.versionName
              }
            }
            """,
            "2.3.4+57",
            GradleDslKind.Kotlin);

        Assert.Equal(ReleaseVersionStatus.Succeeded, result.Status);
        Assert.Equal("2.3.4", result.VersionName?.Value);
        Assert.Equal(57, result.VersionCode?.NumericValue);
        Assert.Equal(ReleaseVersionSourceKind.FlutterPubspecReference, result.VersionName?.SourceKind);
        Assert.Equal(ReleaseVersionSourceKind.FlutterPubspecReference, result.VersionCode?.SourceKind);
        Assert.All(result.Evidence, item => Assert.NotNull(item.PubspecPath));
    }

    [Fact]
    public void Parse_ModernGroovyFlutterReferences_ResolvesFromPubspec()
    {
        var result = Parse(
            """
            android {
              defaultConfig {
                versionCode flutter.versionCode
                versionName flutter.versionName
              }
            }
            """,
            "1.8.0+19");

        Assert.True(result.IsSuccess);
        Assert.Equal("1.8.0", result.VersionName?.Value);
        Assert.Equal("19", result.VersionCode?.Value);
    }

    [Fact]
    public void Parse_LegacyGroovyFlutterVariables_ResolvesFromPubspec()
    {
        var result = Parse(
            """
            def flutterVersionCode = localProperties.getProperty('flutter.versionCode')
            def flutterVersionName = localProperties.getProperty('flutter.versionName')
            android {
              defaultConfig {
                versionCode flutterVersionCode.toInteger()
                versionName flutterVersionName
              }
            }
            """,
            "4.0.1+312");

        Assert.Equal(ReleaseVersionStatus.Succeeded, result.Status);
        Assert.Equal("4.0.1", result.VersionName?.Value);
        Assert.Equal(312, result.VersionCode?.NumericValue);
    }

    [Fact]
    public void Parse_StaticGroovyValues_AreTypedAsGradleEvidence()
    {
        var result = Parse(
            """
            android {
              defaultConfig {
                versionCode 42
                versionName '3.7.9'
              }
            }
            """,
            "1.0.0+1");

        Assert.Equal(ReleaseVersionStatus.Succeeded, result.Status);
        Assert.Equal("3.7.9", result.VersionName?.Value);
        Assert.Equal(42, result.VersionCode?.NumericValue);
        Assert.Equal(ReleaseVersionSourceKind.StaticGradle, result.VersionName?.SourceKind);
        Assert.Equal(ReleaseVersionSourceKind.StaticGradle, result.VersionCode?.SourceKind);
        Assert.All(result.Evidence, item => Assert.Null(item.PubspecPath));
    }

    [Fact]
    public void Parse_StaticKotlinAssignments_AreSupported()
    {
        var result = Parse(
            """
            android {
              defaultConfig {
                versionCode = 77
                versionName = "7.7.0"
              }
            }
            """,
            "1.0.0+1",
            GradleDslKind.Kotlin);

        Assert.Equal(ReleaseVersionStatus.Succeeded, result.Status);
        Assert.Equal("7.7.0", result.VersionName?.Value);
        Assert.Equal(77, result.VersionCode?.NumericValue);
    }

    [Fact]
    public void Parse_ParenthesizedStaticValues_AreSupported()
    {
        var result = Parse(
            """
            android {
              defaultConfig {
                versionCode(13)
                versionName("1.3.0")
              }
            }
            """,
            "1.0.0+1",
            GradleDslKind.Kotlin);

        Assert.Equal(ReleaseVersionStatus.Succeeded, result.Status);
        Assert.Equal("1.3.0", result.VersionName?.Value);
        Assert.Equal(13, result.VersionCode?.NumericValue);
    }

    [Fact]
    public void Parse_IdenticalStaticAndFlutterValues_PrefersStaticEvidenceWithoutAmbiguity()
    {
        var result = Parse(
            """
            android {
              defaultConfig {
                versionName "2.0.0"
                versionName flutter.versionName
                versionCode 20
                versionCode flutter.versionCode
              }
            }
            """,
            "2.0.0+20");

        Assert.Equal(ReleaseVersionStatus.Succeeded, result.Status);
        Assert.Equal(ReleaseVersionSourceKind.StaticGradle, result.VersionName?.SourceKind);
        Assert.Equal(ReleaseVersionSourceKind.StaticGradle, result.VersionCode?.SourceKind);
        Assert.Equal(4, result.Evidence.Count);
    }

    [Fact]
    public void Parse_ConflictingStaticAndFlutterValues_ReturnsAmbiguous()
    {
        var result = Parse(
            """
            android {
              defaultConfig {
                versionName "9.9.9"
                versionName flutter.versionName
                versionCode 999
                versionCode flutter.versionCode
              }
            }
            """,
            "1.2.3+7");

        Assert.Equal(ReleaseVersionStatus.Ambiguous, result.Status);
        Assert.Null(result.VersionName);
        Assert.Null(result.VersionCode);
    }

    [Fact]
    public void Parse_MissingPubspecBuildNumber_LeavesFlutterVersionCodeUnresolved()
    {
        var result = Parse(
            """
            android {
              defaultConfig {
                versionName = flutter.versionName
                versionCode = flutter.versionCode
              }
            }
            """,
            "6.1.0",
            GradleDslKind.Kotlin);

        Assert.Equal(ReleaseVersionStatus.Partial, result.Status);
        Assert.Equal("6.1.0", result.VersionName?.Value);
        Assert.Null(result.VersionCode);
        Assert.Contains(ReleaseVersionField.VersionCode, result.UnresolvedFields);
    }

    [Theory]
    [InlineData("1.2.3+0")]
    [InlineData("1.2.3+2147483648")]
    [InlineData("1.2.3+1.2")]
    [InlineData("1.2.3+abc")]
    public void Parse_InvalidAndroidPubspecBuildNumber_DoesNotGuessVersionCode(string pubspecVersion)
    {
        var result = Parse(
            """
            android {
              defaultConfig {
                versionName flutter.versionName
                versionCode flutter.versionCode
              }
            }
            """,
            pubspecVersion);

        Assert.Equal(ReleaseVersionStatus.Partial, result.Status);
        Assert.NotNull(result.VersionName);
        Assert.Null(result.VersionCode);
        Assert.Contains(ReleaseVersionField.VersionCode, result.UnresolvedFields);
    }

    [Fact]
    public void Parse_DynamicVersionName_ReturnsPartialWithoutGuessing()
    {
        var result = Parse(
            """
            android {
              defaultConfig {
                versionName project.findProperty("releaseName")
                versionCode 8
              }
            }
            """,
            "1.0.0+1");

        Assert.Equal(ReleaseVersionStatus.Partial, result.Status);
        Assert.Null(result.VersionName);
        Assert.Equal(8, result.VersionCode?.NumericValue);
        Assert.Contains(ReleaseVersionField.VersionName, result.UnresolvedFields);
    }

    [Fact]
    public void Parse_InterpolatedOrConcatenatedName_IsUnresolved()
    {
        var result = Parse(
            """
            android {
              defaultConfig {
                versionName "1.${minor}.0"
                versionCode 8
              }
            }
            """,
            "1.0.0+1");

        Assert.Equal(ReleaseVersionStatus.Partial, result.Status);
        Assert.Null(result.VersionName);

        var concatenated = Parse(
            """
            android {
              defaultConfig {
                versionName "1." + minor
                versionCode 8
              }
            }
            """,
            "1.0.0+1");

        Assert.Equal(ReleaseVersionStatus.Partial, concatenated.Status);
        Assert.Null(concatenated.VersionName);
    }

    [Fact]
    public void Parse_HelperVariablesNamedLikeDslFields_AreIgnored()
    {
        var result = Parse(
            """
            android {
              defaultConfig {
                def versionName = "99.0.0"
                def versionCode = 999
                versionName "3.0.0"
                versionCode 30
              }
            }
            """,
            "1.0.0+1");

        Assert.Equal(ReleaseVersionStatus.Succeeded, result.Status);
        Assert.Equal("3.0.0", result.VersionName?.Value);
        Assert.Equal(30, result.VersionCode?.NumericValue);
        Assert.DoesNotContain(result.Evidence, item => item.Value == "99.0.0" || item.Value == "999");
    }

    [Fact]
    public void Parse_StaticPlusDynamicSameField_DoesNotTreatStaticAsEffective()
    {
        var result = Parse(
            """
            android {
              defaultConfig {
                versionName "3.0.0"
                versionName project.findProperty("releaseName")
                versionCode 30
              }
            }
            """,
            "1.0.0+1");

        Assert.Equal(ReleaseVersionStatus.Partial, result.Status);
        Assert.Null(result.VersionName);
        Assert.Equal(30, result.VersionCode?.NumericValue);
        Assert.Contains(ReleaseVersionField.VersionName, result.UnresolvedFields);
        Assert.Contains(result.Evidence, item => item.Field == ReleaseVersionField.VersionName && item.Value == "3.0.0");
    }

    [Fact]
    public void Parse_BuildTypeAndFlavorOverrides_AreExcludedFromDefaultVersion()
    {
        var result = Parse(
            """
            android {
              defaultConfig {
                versionName "1.0.0"
                versionCode 1
              }
              buildTypes {
                release {
                  versionName "99.0.0"
                  versionCode 99
                }
              }
              productFlavors {
                demo {
                  versionName "77.0.0"
                  versionCode 77
                }
              }
            }
            """,
            "8.0.0+8");

        Assert.Equal(ReleaseVersionStatus.Succeeded, result.Status);
        Assert.Equal("1.0.0", result.VersionName?.Value);
        Assert.Equal(1, result.VersionCode?.NumericValue);
        Assert.Equal(2, result.Evidence.Count);
    }

    [Fact]
    public void Parse_CommentsAndQuotedLookalikes_AreIgnored()
    {
        var result = Parse(
            """
            def note = "versionName '99.0.0' versionCode 999"
            android {
              defaultConfig {
                // versionName "88.0.0"
                /* versionCode 888 */
                versionName "1.4.0" // real value
                versionCode 14
              }
            }
            """,
            "1.0.0+1");

        Assert.Equal(ReleaseVersionStatus.Succeeded, result.Status);
        Assert.Equal("1.4.0", result.VersionName?.Value);
        Assert.Equal(14, result.VersionCode?.NumericValue);
        Assert.Equal(2, result.Evidence.Count);
    }

    [Fact]
    public void Parse_MissingSuccessfulPubspecEvidence_StopsBeforeGradleInference()
    {
        var android = Path.Combine(_root, "android");
        var app = Path.Combine(android, "app");
        Directory.CreateDirectory(app);
        var script = Path.Combine(app, "build.gradle");
        File.WriteAllText(script, "android { defaultConfig { versionName '1.0.0'; versionCode 1 } }");

        var failedPubspec = new PubspecParseResult(
            PubspecParseStatus.MalformedYaml,
            SuccessfulRoot(),
            Path.Combine(_root, "pubspec.yaml"),
            null,
            null,
            "bad yaml");

        var result = new ReleaseVersionParser().Parse(
            failedPubspec,
            Dsl(new GradleScriptEvidence(GradleScriptRole.AppBuild, GradleDslKind.Groovy, script)));

        Assert.Equal(ReleaseVersionStatus.PubspecUnavailable, result.Status);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public void ResultContract_DoesNotExposeRawGradleSource()
    {
        Assert.DoesNotContain(
            typeof(ReleaseVersionResult).GetProperties(),
            property => property.Name.Contains("Raw", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("Content", StringComparison.OrdinalIgnoreCase));
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

    private ReleaseVersionResult Parse(
        string content,
        string? pubspecVersion,
        GradleDslKind dsl = GradleDslKind.Groovy)
    {
        var android = Path.Combine(_root, "android");
        var app = Path.Combine(android, "app");
        Directory.CreateDirectory(app);
        var path = Path.Combine(app, dsl == GradleDslKind.Kotlin ? "build.gradle.kts" : "build.gradle");
        File.WriteAllText(path, content);

        return new ReleaseVersionParser().Parse(
            Pubspec(pubspecVersion),
            Dsl(new GradleScriptEvidence(GradleScriptRole.AppBuild, dsl, path)));
    }

    private PubspecParseResult Pubspec(string? version)
    {
        var path = Path.Combine(_root, "pubspec.yaml");
        var metadata = new PubspecMetadata(
            "sample_app",
            null,
            version,
            null,
            null,
            null,
            null,
            null,
            ">=3.0.0 <4.0.0",
            null,
            Array.Empty<string>(),
            Array.Empty<PubspecDependency>());

        return new PubspecParseResult(
            PubspecParseStatus.Succeeded,
            SuccessfulRoot(),
            path,
            metadata,
            null,
            "Test pubspec.");
    }

    private GradleDslDetectionResult Dsl(params GradleScriptEvidence[] scripts)
        => new(
            GradleDslDetectionStatus.Succeeded,
            SuccessfulRoot(),
            Path.Combine(_root, "android"),
            scripts.Select(script => script.Dsl).Distinct().Count() == 1 ? scripts[0].Dsl : GradleDslKind.Mixed,
            scripts,
            "Test Gradle DSL.");

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
