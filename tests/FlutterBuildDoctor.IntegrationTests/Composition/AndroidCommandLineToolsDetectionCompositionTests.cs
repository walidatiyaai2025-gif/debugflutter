using FlutterBuildDoctor.Android.Detection;
using FlutterBuildDoctor.App.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Composition;

public sealed class AndroidCommandLineToolsDetectionCompositionTests
{
    [Fact]
    public void RuntimeDetection_ResolvesSingletonCommandLineToolsDetector()
    {
        var services = new ServiceCollection();
        services.AddFlutterBuildDoctorRuntimeDetection();
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IAndroidCommandLineToolsDetector>();
        var second = provider.GetRequiredService<IAndroidCommandLineToolsDetector>();

        Assert.IsType<AndroidCommandLineToolsDetector>(first);
        Assert.Same(first, second);
    }
}
