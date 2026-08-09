using FlutterBuildDoctor.App.DependencyInjection;
using FlutterBuildDoctor.Flutter.ProjectAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Composition;

public sealed class LocalPropertiesDetectorCompositionTests
{
    [Fact]
    public void RuntimeDetectionComposition_ResolvesDetectorAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFlutterBuildDoctorRuntimeDetection();

        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<ILocalPropertiesDetector>();
        var second = provider.GetRequiredService<ILocalPropertiesDetector>();

        Assert.IsType<LocalPropertiesDetector>(first);
        Assert.Same(first, second);
    }
}
