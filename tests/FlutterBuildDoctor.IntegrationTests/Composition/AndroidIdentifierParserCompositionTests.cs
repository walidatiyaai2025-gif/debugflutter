using FlutterBuildDoctor.App.DependencyInjection;
using FlutterBuildDoctor.Flutter.ProjectAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Composition;

public sealed class AndroidIdentifierParserCompositionTests
{
    [Fact]
    public void RuntimeDetectionComposition_ResolvesParserAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFlutterBuildDoctorRuntimeDetection();

        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IAndroidIdentifierParser>();
        var second = provider.GetRequiredService<IAndroidIdentifierParser>();

        Assert.IsType<AndroidIdentifierParser>(first);
        Assert.Same(first, second);
    }
}
