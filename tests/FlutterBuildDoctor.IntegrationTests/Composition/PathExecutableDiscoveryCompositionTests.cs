using FlutterBuildDoctor.App.DependencyInjection;
using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Infrastructure.Environment;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Composition;

public sealed class PathExecutableDiscoveryCompositionTests
{
    [Fact]
    public void RuntimeDetectionComposition_ResolvesWindowsPathExecutableDiscoveryAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFlutterBuildDoctorRuntimeDetection();

        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IPathExecutableDiscovery>();
        var second = provider.GetRequiredService<IPathExecutableDiscovery>();

        Assert.IsType<WindowsPathExecutableDiscovery>(first);
        Assert.Same(first, second);
    }
}
