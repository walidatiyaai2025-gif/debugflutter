using FlutterBuildDoctor.Android.Detection;
using FlutterBuildDoctor.App.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Composition;

public sealed class AndroidAvdManagerDetectionCompositionTests
{
    [Fact]
    public void RuntimeDetection_ResolvesSingletonAvdManagerDetector()
    {
        var services = new ServiceCollection();
        services.AddFlutterBuildDoctorRuntimeDetection();
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IAndroidAvdManagerDetector>();
        var second = provider.GetRequiredService<IAndroidAvdManagerDetector>();

        Assert.IsType<AndroidAvdManagerDetector>(first);
        Assert.Same(first, second);
    }
}
