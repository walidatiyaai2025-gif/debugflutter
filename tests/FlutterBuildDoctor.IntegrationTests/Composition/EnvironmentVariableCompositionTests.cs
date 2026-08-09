using FlutterBuildDoctor.App.DependencyInjection;
using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Infrastructure.Environment;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Composition;

public sealed class EnvironmentVariableCompositionTests
{
    [Fact]
    public void RuntimeDetection_ResolvesEnvironmentReaderAndSystemSourceAsSingletons()
    {
        var services = new ServiceCollection();
        services.AddFlutterBuildDoctorRuntimeDetection();
        using var provider = services.BuildServiceProvider();

        var reader = provider.GetRequiredService<IEnvironmentVariableReader>();
        var source = provider.GetRequiredService<IVariableValueSource>();

        Assert.IsType<EnvironmentVariableReader>(reader);
        Assert.IsType<SystemVariableValueSource>(source);
        Assert.Same(reader, provider.GetRequiredService<IEnvironmentVariableReader>());
        Assert.Same(source, provider.GetRequiredService<IVariableValueSource>());
    }
}
