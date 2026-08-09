using FlutterBuildDoctor.App.DependencyInjection;
using FlutterBuildDoctor.Flutter.ProjectAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Composition;

public sealed class FlutterProjectRootCompositionTests
{
    [Fact]
    public void RuntimeDetectionComposition_ResolvesProjectRootLocatorAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFlutterBuildDoctorRuntimeDetection();

        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IFlutterProjectRootLocator>();
        var second = provider.GetRequiredService<IFlutterProjectRootLocator>();

        Assert.IsType<FlutterProjectRootLocator>(first);
        Assert.Same(first, second);
    }
}
