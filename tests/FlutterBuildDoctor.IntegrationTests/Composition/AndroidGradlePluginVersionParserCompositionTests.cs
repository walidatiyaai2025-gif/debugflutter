using FlutterBuildDoctor.App.DependencyInjection;
using FlutterBuildDoctor.Flutter.ProjectAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Composition;

public sealed class AndroidGradlePluginVersionParserCompositionTests
{
    [Fact]
    public void RuntimeDetectionComposition_ResolvesAgpVersionParserAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFlutterBuildDoctorRuntimeDetection();

        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IAndroidGradlePluginVersionParser>();
        var second = provider.GetRequiredService<IAndroidGradlePluginVersionParser>();

        Assert.IsType<AndroidGradlePluginVersionParser>(first);
        Assert.Same(first, second);
    }
}
