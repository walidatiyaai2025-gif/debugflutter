using FlutterBuildDoctor.App.DependencyInjection;
using FlutterBuildDoctor.Flutter.ProjectAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Composition;

public sealed class GradleDslDetectorCompositionTests
{
    [Fact]
    public void RuntimeDetectionComposition_ResolvesGradleDslDetectorAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFlutterBuildDoctorRuntimeDetection();

        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IGradleDslDetector>();
        var second = provider.GetRequiredService<IGradleDslDetector>();

        Assert.IsType<GradleDslDetector>(first);
        Assert.Same(first, second);
    }
}
