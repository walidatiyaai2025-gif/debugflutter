using FlutterBuildDoctor.Android.Detection;
using FlutterBuildDoctor.App.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Composition;

public sealed class AndroidSdkRootDetectionCompositionTests
{
    [Fact]
    public void RuntimeDetection_ResolvesSingletonAndroidSdkRootDetector()
    {
        var services = new ServiceCollection();
        services.AddFlutterBuildDoctorRuntimeDetection();
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IAndroidSdkRootDetector>();
        var second = provider.GetRequiredService<IAndroidSdkRootDetector>();

        Assert.IsType<AndroidSdkRootDetector>(first);
        Assert.Same(first, second);
    }
}
