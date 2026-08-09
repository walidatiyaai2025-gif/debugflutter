using FlutterBuildDoctor.Android.Detection;
using FlutterBuildDoctor.App.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Composition;

public sealed class AndroidLicenseDetectionCompositionTests
{
    [Fact]
    public void RuntimeDetection_ResolvesSingletonAndroidLicenseDetector()
    {
        var services = new ServiceCollection();
        services.AddFlutterBuildDoctorRuntimeDetection();
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IAndroidLicenseDetector>();
        var second = provider.GetRequiredService<IAndroidLicenseDetector>();

        Assert.IsType<AndroidLicenseDetector>(first);
        Assert.Same(first, second);
    }
}
