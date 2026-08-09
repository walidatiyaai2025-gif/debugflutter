using FlutterBuildDoctor.App.DependencyInjection;
using FlutterBuildDoctor.Flutter.ProjectAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Composition;

public sealed class ReleaseVersionParserCompositionTests
{
    [Fact]
    public void RuntimeDetectionComposition_ResolvesParserAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFlutterBuildDoctorRuntimeDetection();

        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IReleaseVersionParser>();
        var second = provider.GetRequiredService<IReleaseVersionParser>();

        Assert.IsType<ReleaseVersionParser>(first);
        Assert.Same(first, second);
    }
}
