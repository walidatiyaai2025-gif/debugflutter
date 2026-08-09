using FlutterBuildDoctor.Flutter.ProjectAnalysis;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class DartEntryTargetDetectorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "fbd-dart-entry-" + Guid.NewGuid().ToString("N"));

    public DartEntryTargetDetectorTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "lib"));
    }

    [Fact]
    public void Detect_CanonicalMain_IsRunnableAndProjectRelative()
    {
        Write("lib/main.dart", "void main() { print('ok'); }");

        var result = Detect();

        Assert.Equal(DartEntryTargetDetectionStatus.Succeeded, result.Status);
        var target = Assert.Single(result.Targets);
        Assert.True(target.IsRunnable);
        Assert.Equal(DartEntryTargetKind.CanonicalMain, target.Kind);
        Assert.Equal("lib/main.dart", target.RelativeTargetPath);
        Assert.Null(target.FlavorHint);
    }

    [Theory]
    [InlineData("Future<void> main() async { }")]
    [InlineData("void main(List<String> args) => runApp(const App());")]
    [InlineData("main() { }")]
    [InlineData("Future<void> main() async => bootstrap();")]
    public void Detect_CommonTopLevelMainSignatures_AreRunnable(string source)
    {
        Write("lib/main.dart", source);

        var target = Assert.Single(Detect().Targets);

        Assert.Equal(DartEntryTargetInspectionStatus.Runnable, target.InspectionStatus);
    }

    [Fact]
    public void Detect_FlavorStyleNames_ExposeConservativeHints()
    {
        Write("lib/main_dev.dart", "void main() {}");
        Write("lib/main.staging.dart", "void main() {}");
        Write("lib/main-prod.dart", "void main() {}");

        var result = Detect();

        Assert.Equal(DartEntryTargetDetectionStatus.Succeeded, result.Status);
        Assert.Equal(3, result.Targets.Count);
        Assert.Equal("dev", Find(result, "lib/main_dev.dart").FlavorHint);
        Assert.Equal("staging", Find(result, "lib/main.staging.dart").FlavorHint);
        Assert.Equal("prod", Find(result, "lib/main-prod.dart").FlavorHint);
        Assert.All(result.Targets, target => Assert.Equal(DartEntryTargetKind.ConventionalFlavorMain, target.Kind));
    }

    [Fact]
    public void Detect_NestedMain_IsSupportedWithinBoundedLibScan()
    {
        Write("lib/variants/demo/main.dart", "void main() {}");

        var result = Detect();

        Assert.Equal(DartEntryTargetDetectionStatus.Succeeded, result.Status);
        var target = Assert.Single(result.Targets);
        Assert.Equal(DartEntryTargetKind.NestedMain, target.Kind);
        Assert.Equal("lib/variants/demo/main.dart", target.RelativeTargetPath);
    }

    [Fact]
    public void Detect_MainLikeFilenameWithoutMain_IsTypedNonRunnable()
    {
        Write("lib/main_demo.dart", "void bootstrap() {}");

        var result = Detect();

        Assert.Equal(DartEntryTargetDetectionStatus.Partial, result.Status);
        var target = Assert.Single(result.Targets);
        Assert.Equal(DartEntryTargetInspectionStatus.MainDeclarationMissing, target.InspectionStatus);
        Assert.False(target.IsRunnable);
        Assert.Equal("demo", target.FlavorHint);
    }

    [Fact]
    public void Detect_CommentsAndStringLookalikes_DoNotCountAsMain()
    {
        Write(
            "lib/main_fake.dart",
            """
            // void main() {}
            const text = "void main() {}";
            const raw = r'Future<void> main() async {}';
            const triple = '''main() { }''';
            void bootstrap() {}
            """);

        var target = Assert.Single(Detect().Targets);

        Assert.Equal(DartEntryTargetInspectionStatus.MainDeclarationMissing, target.InspectionStatus);
    }

    [Fact]
    public void Detect_MainMethodInsideClass_DoesNotCountAsTopLevelEntry()
    {
        Write(
            "lib/main_class.dart",
            """
            class Program {
              void main() {}
            }
            """);

        var target = Assert.Single(Detect().Targets);

        Assert.Equal(DartEntryTargetInspectionStatus.MainDeclarationMissing, target.InspectionStatus);
    }

    [Fact]
    public void Detect_NonMainDartFiles_AreNotCandidates()
    {
        Write("lib/app.dart", "void main() {}");
        Write("lib/bootstrap.dart", "void bootstrap() {}");

        var result = Detect();

        Assert.Equal(DartEntryTargetDetectionStatus.NoTargets, result.Status);
        Assert.Empty(result.Targets);
    }

    [Fact]
    public void Detect_RunnableAndNonRunnableCandidates_ReturnPartialButPreserveBoth()
    {
        Write("lib/main.dart", "void main() {}");
        Write("lib/main_broken.dart", "void bootstrap() {}");

        var result = Detect();

        Assert.Equal(DartEntryTargetDetectionStatus.Partial, result.Status);
        Assert.Equal(2, result.Targets.Count);
        Assert.Single(result.RunnableTargets);
        Assert.Equal("lib/main.dart", result.RunnableTargets[0].RelativeTargetPath);
    }

    [Fact]
    public void Detect_TargetOrdering_IsCanonicalThenFlavorThenNested()
    {
        Write("lib/z/main.dart", "void main() {}");
        Write("lib/main_prod.dart", "void main() {}");
        Write("lib/main.dart", "void main() {}");
        Write("lib/main_dev.dart", "void main() {}");

        var paths = Detect().Targets.Select(target => target.RelativeTargetPath).ToArray();

        Assert.Equal(
            new[]
            {
                "lib/main.dart",
                "lib/main_dev.dart",
                "lib/main_prod.dart",
                "lib/z/main.dart"
            },
            paths);
    }

    [Fact]
    public void ResultContract_DoesNotExposeRawDartSource()
    {
        Assert.DoesNotContain(
            typeof(DartEntryTargetDetectionResult).GetProperties(),
            property => property.Name.Contains("Raw", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("Content", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("Source", StringComparison.OrdinalIgnoreCase));
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

    private DartEntryTargetDetectionResult Detect()
        => new DartEntryTargetDetector().Detect(SuccessfulRoot());

    private DartEntryTarget Find(DartEntryTargetDetectionResult result, string path)
        => Assert.Single(result.Targets.Where(target => target.RelativeTargetPath == path));

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
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
