using FlutterBuildDoctor.App.DependencyInjection;
using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Infrastructure.Environment;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Composition;

public sealed class WindowsEnvironmentDetectionCompositionTests
{
    [Fact]
    public void RuntimeDetection_ResolvesSingletonWindowsEnvironmentDetector()
    {
        var services = new ServiceCollection();
        services.AddFlutterBuildDoctorRuntimeDetection();
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IWindowsEnvironmentDetector>();
        var second = provider.GetRequiredService<IWindowsEnvironmentDetector>();
        var source = provider.GetRequiredService<IWindowsRuntimeInfoSource>();

        Assert.IsType<WindowsEnvironmentDetector>(first);
        Assert.IsType<SystemWindowsRuntimeInfoSource>(source);
        Assert.Same(first, second);
    }
}
