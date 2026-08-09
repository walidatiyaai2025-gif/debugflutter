using FlutterBuildDoctor.App.DependencyInjection;
using FlutterBuildDoctor.Flutter.ProjectAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Composition;

public sealed class ProductFlavorDetectorCompositionTests
{
    [Fact]
    public void RuntimeDetectionComposition_ResolvesDetectorAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFlutterBuildDoctorRuntimeDetection();

        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IProductFlavorDetector>();
        var second = provider.GetRequiredService<IProductFlavorDetector>();

        Assert.IsType<ProductFlavorDetector>(first);
        Assert.Same(first, second);
    }
}
