using FlutterBuildDoctor.Android.Detection;
using FlutterBuildDoctor.App.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Composition;

public sealed class AndroidAdbDetectionCompositionTests
{
    [Fact]
    public void RuntimeDetection_ResolvesSingletonAdbDetector()
    {
        var services = new ServiceCollection();
        services.AddFlutterBuildDoctorRuntimeDetection();
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IAndroidAdbDetector>();
        var second = provider.GetRequiredService<IAndroidAdbDetector>();

        Assert.IsType<AndroidAdbDetector>(first);
        Assert.Same(first, second);
    }
}
