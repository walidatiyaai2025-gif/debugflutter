using FlutterBuildDoctor.Android.Detection;
using FlutterBuildDoctor.App.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Composition;

public sealed class AndroidPlatformDetectionCompositionTests
{
    [Fact]
    public void RuntimeDetection_ResolvesSingletonAndroidPlatformDetector()
    {
        var services = new ServiceCollection();
        services.AddFlutterBuildDoctorRuntimeDetection();
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IAndroidPlatformDetector>();
        var second = provider.GetRequiredService<IAndroidPlatformDetector>();

        Assert.IsType<AndroidPlatformDetector>(first);
        Assert.Same(first, second);
    }
}
