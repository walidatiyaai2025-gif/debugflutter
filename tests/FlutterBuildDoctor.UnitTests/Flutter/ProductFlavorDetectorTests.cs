using FlutterBuildDoctor.Flutter.ProjectAnalysis;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class ProductFlavorDetectorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "fbd-product-flavor-" + Guid.NewGuid().ToString("N"));

    public ProductFlavorDetectorTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Detect_GroovyFlavors_EnumeratesStaticMetadata()
    {
        var result = Detect(
            """
            android {
              flavorDimensions "version"
              productFlavors {
                demo {
                  dimension "version"
                  applicationIdSuffix ".demo"
                  versionNameSuffix "-demo"
                }
                full {
                  dimension "version"
                  applicationId "com.example.full"
                }
              }
            }
            """);

        Assert.Equal(ProductFlavorDetectionStatus.Succeeded, result.Status);
        Assert.Equal(new[] { "version" }, result.DeclaredDimensions);
        Assert.Equal(new[] { "demo", "full" }, result.Flavors.Select(flavor => flavor.Name));

        var demo = Assert.Single(result.Flavors.Where(flavor => flavor.Name == "demo"));
        Assert.Equal("version", demo.Dimension?.Value);
        Assert.Equal(".demo", demo.ApplicationIdSuffix?.Value);
        Assert.Equal("-demo", demo.VersionNameSuffix?.Value);

        var full = Assert.Single(result.Flavors.Where(flavor => flavor.Name == "full"));
        Assert.Equal("com.example.full", full.ApplicationId?.Value);
        Assert.Empty(full.UnresolvedFields);
    }

    [Fact]
    public void Detect_KotlinCreateFlavors_EnumeratesStaticMetadata()
    {
        var result = Detect(
            """
            android {
              flavorDimensions += "version"
              productFlavors {
                create("demo") {
                  dimension = "version"
                  applicationIdSuffix = ".demo"
                  versionNameSuffix = "-demo"
                }
                create("full") {
                  dimension = "version"
                }
              }
            }
            """,
            GradleDslKind.Kotlin);

        Assert.Equal(ProductFlavorDetectionStatus.Succeeded, result.Status);
        Assert.Equal(new[] { "demo", "full" }, result.Flavors.Select(flavor => flavor.Name));
        Assert.All(result.Flavors, flavor => Assert.Equal("version", flavor.Dimension?.Value));
    }

    [Fact]
    public void Detect_SingleDeclaredDimension_IsInferredWhenFlavorOmitsIt()
    {
        var result = Detect(
            """
            android {
              flavorDimensions "version"
              productFlavors {
                demo {
                  applicationIdSuffix ".demo"
                }
              }
            }
            """);

        Assert.Equal(ProductFlavorDetectionStatus.Succeeded, result.Status);
        var demo = Assert.Single(result.Flavors);
        Assert.Equal("version", demo.Dimension?.Value);
        Assert.Equal(ProductFlavorValueSourceKind.InferredSingleDimension, demo.Dimension?.SourceKind);
        Assert.Contains(
            result.Evidence,
            item => item.FlavorName == "demo" &&
                    item.Field == ProductFlavorField.Dimension &&
                    item.SourceKind == ProductFlavorValueSourceKind.InferredSingleDimension);
    }

    [Fact]
    public void Detect_MultipleDimensions_DoNotInferMissingFlavorDimension()
    {
        var result = Detect(
            """
            android {
              flavorDimensions "api", "mode"
              productFlavors {
                minApi24 {
                  applicationIdSuffix ".api24"
                }
              }
            }
            """);

        Assert.Equal(ProductFlavorDetectionStatus.Succeeded, result.Status);
        Assert.Equal(new[] { "api", "mode" }, result.DeclaredDimensions);
        var flavor = Assert.Single(result.Flavors);
        Assert.Null(flavor.Dimension);
    }

    [Fact]
    public void Detect_KotlinListOfDimensions_AreParsed()
    {
        var result = Detect(
            """
            android {
              flavorDimensions += listOf("api", "mode")
              productFlavors {
                create("demo") { dimension = "mode" }
              }
            }
            """,
            GradleDslKind.Kotlin);

        Assert.Equal(ProductFlavorDetectionStatus.Succeeded, result.Status);
        Assert.Equal(new[] { "api", "mode" }, result.DeclaredDimensions);
        Assert.Equal("mode", Assert.Single(result.Flavors).Dimension?.Value);
    }

    [Fact]
    public void Detect_DynamicFlavorName_IsPreservedAsPartialWithoutGuessing()
    {
        var result = Detect(
            """
            android {
              flavorDimensions "version"
              productFlavors {
                create(flavorName) { dimension "version" }
                demo { dimension "version" }
              }
            }
            """);

        Assert.Equal(ProductFlavorDetectionStatus.Partial, result.Status);
        Assert.Equal(1, result.UnresolvedFlavorDeclarations);
        Assert.Equal("demo", Assert.Single(result.Flavors).Name);
    }

    [Fact]
    public void Detect_StaticPlusDynamicField_DoesNotTreatStaticValueAsEffective()
    {
        var result = Detect(
            """
            android {
              flavorDimensions "version"
              productFlavors {
                demo {
                  dimension "version"
                  applicationIdSuffix ".demo"
                  applicationIdSuffix project.findProperty("suffix")
                }
              }
            }
            """);

        Assert.Equal(ProductFlavorDetectionStatus.Partial, result.Status);
        var flavor = Assert.Single(result.Flavors);
        Assert.Null(flavor.ApplicationIdSuffix);
        Assert.Contains(ProductFlavorField.ApplicationIdSuffix, flavor.UnresolvedFields);
        Assert.Contains(
            result.Evidence,
            item => item.FlavorName == "demo" &&
                    item.Field == ProductFlavorField.ApplicationIdSuffix &&
                    item.Value == ".demo");
    }

    [Fact]
    public void Detect_ConflictingStaticFieldValues_ReturnsAmbiguous()
    {
        var result = Detect(
            """
            android {
              productFlavors {
                demo {
                  applicationIdSuffix ".one"
                  applicationIdSuffix ".two"
                }
              }
            }
            """);

        Assert.Equal(ProductFlavorDetectionStatus.Ambiguous, result.Status);
        Assert.Null(Assert.Single(result.Flavors).ApplicationIdSuffix);
    }

    [Fact]
    public void Detect_DuplicateFlavorName_ReturnsAmbiguous()
    {
        var result = Detect(
            """
            android {
              productFlavors {
                demo { applicationIdSuffix ".one" }
                demo { applicationIdSuffix ".two" }
              }
            }
            """);

        Assert.Equal(ProductFlavorDetectionStatus.Ambiguous, result.Status);
        Assert.Single(result.Flavors);
    }

    [Fact]
    public void Detect_CommentsStringsAndOtherAndroidBlocks_AreIgnored()
    {
        var result = Detect(
            """
            def note = "productFlavors { fake { applicationIdSuffix '.fake' } }"
            android {
              // productFlavors { commentFake { } }
              buildTypes {
                release {
                  applicationIdSuffix ".release"
                }
              }
              productFlavors {
                demo {
                  applicationIdSuffix ".demo"
                }
              }
            }
            """);

        Assert.Equal(ProductFlavorDetectionStatus.Succeeded, result.Status);
        var flavor = Assert.Single(result.Flavors);
        Assert.Equal("demo", flavor.Name);
        Assert.Equal(".demo", flavor.ApplicationIdSuffix?.Value);
        Assert.DoesNotContain(result.Evidence, item => item.Value.Contains("fake", StringComparison.Ordinal));
    }

    [Fact]
    public void Detect_ContainerHelperBlocks_AreNotInventedAsFlavors()
    {
        var result = Detect(
            """
            android {
              productFlavors {
                all { }
                configureEach { }
              }
            }
            """);

        Assert.Equal(ProductFlavorDetectionStatus.NoFlavors, result.Status);
        Assert.Empty(result.Flavors);
    }

    [Fact]
    public void Detect_NoProductFlavorsBlock_ReturnsSuccessfulNoFlavors()
    {
        var result = Detect(
            """
            android {
              defaultConfig {
                applicationId "com.example.app"
              }
            }
            """);

        Assert.True(result.IsSuccess);
        Assert.Equal(ProductFlavorDetectionStatus.NoFlavors, result.Status);
        Assert.Empty(result.Flavors);
    }

    [Fact]
    public void Detect_EmptyProductFlavorsBlock_ReturnsSuccessfulNoFlavors()
    {
        var result = Detect(
            """
            android {
              productFlavors {
              }
            }
            """);

        Assert.Equal(ProductFlavorDetectionStatus.NoFlavors, result.Status);
        Assert.Empty(result.Flavors);
    }

    [Fact]
    public void Detect_ApplicationIdOverrideAndEmptySuffixes_AreRepresented()
    {
        var result = Detect(
            """
            android {
              productFlavors {
                enterprise {
                  applicationId "com.example.enterprise"
                  applicationIdSuffix ""
                  versionNameSuffix ""
                }
              }
            }
            """);

        Assert.Equal(ProductFlavorDetectionStatus.Succeeded, result.Status);
        var flavor = Assert.Single(result.Flavors);
        Assert.Equal("com.example.enterprise", flavor.ApplicationId?.Value);
        Assert.Equal(string.Empty, flavor.ApplicationIdSuffix?.Value);
        Assert.Equal(string.Empty, flavor.VersionNameSuffix?.Value);
    }

    [Fact]
    public void Detect_UnresolvedDimensionDeclaration_PreventsSingleDimensionInference()
    {
        var result = Detect(
            """
            android {
              flavorDimensions "version"
              flavorDimensions dynamicDimensions
              productFlavors {
                demo { applicationIdSuffix ".demo" }
              }
            }
            """);

        Assert.Equal(ProductFlavorDetectionStatus.Partial, result.Status);
        Assert.True(result.HasUnresolvedDimensionDeclarations);
        Assert.Null(Assert.Single(result.Flavors).Dimension);
    }

    [Fact]
    public void ResultContract_DoesNotExposeRawGradleSource()
    {
        Assert.DoesNotContain(
            typeof(ProductFlavorDetectionResult).GetProperties(),
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

    private ProductFlavorDetectionResult Detect(
        string content,
        GradleDslKind dsl = GradleDslKind.Groovy)
    {
        var android = Path.Combine(_root, "android");
        var app = Path.Combine(android, "app");
        Directory.CreateDirectory(app);
        var path = Path.Combine(app, dsl == GradleDslKind.Kotlin ? "build.gradle.kts" : "build.gradle");
        File.WriteAllText(path, content);

        return new ProductFlavorDetector().Detect(
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
