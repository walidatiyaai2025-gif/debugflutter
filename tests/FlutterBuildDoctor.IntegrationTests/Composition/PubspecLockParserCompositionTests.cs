using FlutterBuildDoctor.App.DependencyInjection;
using FlutterBuildDoctor.Flutter.ProjectAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Composition;

public sealed class PubspecLockParserCompositionTests
{
    [Fact]
    public void RuntimeDetectionComposition_ResolvesPubspecLockParserAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFlutterBuildDoctorRuntimeDetection();

        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IPubspecLockParser>();
        var second = provider.GetRequiredService<IPubspecLockParser>();

        Assert.IsType<PubspecLockParser>(first);
        Assert.Same(first, second);
    }
}
