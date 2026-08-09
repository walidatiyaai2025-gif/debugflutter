using FlutterBuildDoctor.Android.Detection;
using FlutterBuildDoctor.App.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Composition;

public sealed class JavaDetectionCompositionTests
{
    [Fact]
    public void RuntimeDetection_ResolvesSingletonJavaDetector()
    {
        var services = new ServiceCollection();
        services.AddFlutterBuildDoctorRuntimeDetection();
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IJavaInstallationDetector>();
        var second = provider.GetRequiredService<IJavaInstallationDetector>();

        Assert.IsType<JavaInstallationDetector>(first);
        Assert.Same(first, second);
    }
}
