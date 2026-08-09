using FlutterBuildDoctor.App.DependencyInjection;
using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Infrastructure.Environment;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Composition;

public sealed class AndroidStudioDetectionCompositionTests
{
    [Fact]
    public void RuntimeDetection_ResolvesSingletonAndroidStudioDetectorAndSources()
    {
        var services = new ServiceCollection();
        services.AddFlutterBuildDoctorRuntimeDetection();
        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<IAndroidStudioDetector>();
        var second = provider.GetRequiredService<IAndroidStudioDetector>();
        var roots = provider.GetRequiredService<IAndroidStudioSearchRootProvider>();
        var source = provider.GetRequiredService<IAndroidStudioInstallationSource>();
        Assert.IsType<AndroidStudioDetector>(first);
        Assert.Same(first, second);
        Assert.IsType<SystemAndroidStudioSearchRootProvider>(roots);
        Assert.IsType<WindowsAndroidStudioInstallationSource>(source);
    }
}
