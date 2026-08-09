using FlutterBuildDoctor.App.DependencyInjection;
using FlutterBuildDoctor.Flutter.Doctor;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Environment;

public sealed class FlutterDoctorParserIntegrationTests
{
    [Fact]
    public void RuntimeDetection_ResolvesSingletonFlutterDoctorParser()
    {
        var services = new ServiceCollection();
        services.AddFlutterBuildDoctorRuntimeDetection();
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IFlutterDoctorParser>();
        var second = provider.GetRequiredService<IFlutterDoctorParser>();

        Assert.IsType<FlutterDoctorParser>(first);
        Assert.Same(first, second);
    }
}
