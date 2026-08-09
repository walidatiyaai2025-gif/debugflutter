using FlutterBuildDoctor.App.DependencyInjection;
using FlutterBuildDoctor.Flutter.ProjectAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Composition;

public sealed class DartEntryTargetDetectorCompositionTests
{
    [Fact]
    public void RuntimeDetectionComposition_ResolvesDetectorAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFlutterBuildDoctorRuntimeDetection();

        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IDartEntryTargetDetector>();
        var second = provider.GetRequiredService<IDartEntryTargetDetector>();

        Assert.IsType<DartEntryTargetDetector>(first);
        Assert.Same(first, second);
    }
}
