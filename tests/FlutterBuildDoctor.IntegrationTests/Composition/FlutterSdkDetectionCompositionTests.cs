using FlutterBuildDoctor.App.DependencyInjection;
using FlutterBuildDoctor.Flutter.Detection;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Composition;

public sealed class FlutterSdkDetectionCompositionTests
{
    [Fact]
    public void RuntimeDetectionComposition_ResolvesFlutterSdkDetectorAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFlutterBuildDoctorRuntimeDetection();

        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IFlutterSdkDetector>();
        var second = provider.GetRequiredService<IFlutterSdkDetector>();

        Assert.IsType<FlutterSdkDetector>(first);
        Assert.Same(first, second);
    }
}
