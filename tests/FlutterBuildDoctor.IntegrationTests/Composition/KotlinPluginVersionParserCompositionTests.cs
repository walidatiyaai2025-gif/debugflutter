using FlutterBuildDoctor.App.DependencyInjection;
using FlutterBuildDoctor.Flutter.ProjectAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Composition;

public sealed class KotlinPluginVersionParserCompositionTests
{
    [Fact]
    public void RuntimeDetectionComposition_ResolvesKotlinParserAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFlutterBuildDoctorRuntimeDetection();

        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IKotlinPluginVersionParser>();
        var second = provider.GetRequiredService<IKotlinPluginVersionParser>();

        Assert.IsType<KotlinPluginVersionParser>(first);
        Assert.Same(first, second);
    }
}
