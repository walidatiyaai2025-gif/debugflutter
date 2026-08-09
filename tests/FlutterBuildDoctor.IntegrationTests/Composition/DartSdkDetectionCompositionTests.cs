using FlutterBuildDoctor.App.DependencyInjection;
using FlutterBuildDoctor.Flutter.Detection;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Composition;

public sealed class DartSdkDetectionCompositionTests
{
    [Fact]
    public void RuntimeDetection_ResolvesSingletonDartSdkDetector()
    {
        var services = new ServiceCollection();
        services.AddFlutterBuildDoctorRuntimeDetection();
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IDartSdkDetector>();
        var second = provider.GetRequiredService<IDartSdkDetector>();

        Assert.IsType<DartSdkDetector>(first);
        Assert.Same(first, second);
    }
}
