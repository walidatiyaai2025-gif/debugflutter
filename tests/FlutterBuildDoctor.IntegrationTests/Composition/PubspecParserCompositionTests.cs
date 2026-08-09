using FlutterBuildDoctor.App.DependencyInjection;
using FlutterBuildDoctor.Flutter.ProjectAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Composition;

public sealed class PubspecParserCompositionTests
{
    [Fact]
    public void RuntimeDetectionComposition_ResolvesPubspecParserAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFlutterBuildDoctorRuntimeDetection();

        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IPubspecParser>();
        var second = provider.GetRequiredService<IPubspecParser>();

        Assert.IsType<PubspecParser>(first);
        Assert.Same(first, second);
    }
}
