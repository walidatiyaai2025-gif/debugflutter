using FlutterBuildDoctor.Android.Detection;
using FlutterBuildDoctor.App.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Composition;

public sealed class AndroidBuildToolsDetectionCompositionTests
{
    [Fact]
    public void RuntimeDetection_ResolvesSingletonAndroidBuildToolsDetector()
    {
        var services = new ServiceCollection();
        services.AddFlutterBuildDoctorRuntimeDetection();
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IAndroidBuildToolsDetector>();
        var second = provider.GetRequiredService<IAndroidBuildToolsDetector>();

        Assert.IsType<AndroidBuildToolsDetector>(first);
        Assert.Same(first, second);
    }
}
