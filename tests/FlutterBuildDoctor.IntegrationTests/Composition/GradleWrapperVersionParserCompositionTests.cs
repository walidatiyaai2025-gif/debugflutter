using FlutterBuildDoctor.App.DependencyInjection;
using FlutterBuildDoctor.Flutter.ProjectAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Composition;

public sealed class GradleWrapperVersionParserCompositionTests
{
    [Fact]
    public void RuntimeDetectionComposition_ResolvesGradleWrapperVersionParserAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFlutterBuildDoctorRuntimeDetection();

        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IGradleWrapperVersionParser>();
        var second = provider.GetRequiredService<IGradleWrapperVersionParser>();

        Assert.IsType<GradleWrapperVersionParser>(first);
        Assert.Same(first, second);
    }
}
