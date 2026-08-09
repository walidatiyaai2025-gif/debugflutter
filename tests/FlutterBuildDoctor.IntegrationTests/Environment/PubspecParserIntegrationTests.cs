using FlutterBuildDoctor.App.DependencyInjection;
using FlutterBuildDoctor.Flutter.ProjectAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Environment;

public sealed class PubspecParserIntegrationTests
{
    [Fact]
    public void RuntimeDetection_ResolvesSingletonPubspecParser()
    {
        var services = new ServiceCollection();
        services.AddFlutterBuildDoctorRuntimeDetection();
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IPubspecParser>();
        var second = provider.GetRequiredService<IPubspecParser>();

        Assert.IsType<PubspecParser>(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void LocatorThenParser_ComposesReadOnlyProjectEvidence()
    {
        var root = Path.Combine(Path.GetTempPath(), "fbd-pubspec-integration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "lib"));
            Directory.CreateDirectory(Path.Combine(root, "android"));
            var pubspecPath = Path.Combine(root, "pubspec.yaml");
            const string raw = """
                               name: integrated_app
                               environment:
                                 sdk: '>=3.8.0 <4.0.0'
                               dependencies:
                                 flutter:
                                   sdk: flutter
                               """;
            File.WriteAllText(pubspecPath, raw);
            var beforeBytes = File.ReadAllBytes(pubspecPath);
            var beforeWriteTime = File.GetLastWriteTimeUtc(pubspecPath);

            var locator = new FlutterProjectRootLocator();
            var rootResult = locator.Locate(root);
            Assert.True(rootResult.IsSuccess, rootResult.Message);

            var parser = new PubspecParser();
            var parseResult = parser.Parse(rootResult);

            Assert.True(parseResult.IsSuccess, parseResult.Message);
            Assert.Equal("integrated_app", parseResult.Metadata!.Name);
            Assert.Equal(">=3.8.0 <4.0.0", parseResult.Metadata.DartSdkConstraint);
            Assert.True(parseResult.Metadata.HasFlutterSdkDependency);
            Assert.Equal(raw, parseResult.RawText);
            Assert.Equal(beforeBytes, File.ReadAllBytes(pubspecPath));
            Assert.Equal(beforeWriteTime, File.GetLastWriteTimeUtc(pubspecPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
