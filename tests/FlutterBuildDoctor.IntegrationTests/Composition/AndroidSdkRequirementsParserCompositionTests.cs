using FlutterBuildDoctor.App.DependencyInjection;
using FlutterBuildDoctor.Flutter.ProjectAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Composition;

public sealed class AndroidSdkRequirementsParserCompositionTests
{
    [Fact]
    public void RuntimeDetectionComposition_ResolvesParserAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFlutterBuildDoctorRuntimeDetection();

        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IAndroidSdkRequirementsParser>();
        var second = provider.GetRequiredService<IAndroidSdkRequirementsParser>();

        Assert.IsType<AndroidSdkRequirementsParser>(first);
        Assert.Same(first, second);
    }
}
