using FlutterBuildDoctor.App.DependencyInjection;
using FlutterBuildDoctor.Flutter.ProjectAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Composition;

public sealed class GradleWrapperParserCompositionTests
{
    [Fact]
    public void RuntimeDetectionComposition_ResolvesGradleWrapperParserAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFlutterBuildDoctorRuntimeDetection();

        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IGradleWrapperParser>();
        var second = provider.GetRequiredService<IGradleWrapperParser>();

        Assert.IsType<GradleWrapperParser>(first);
        Assert.Same(first, second);
    }
}
