using FlutterBuildDoctor.App.DependencyInjection;
using FlutterBuildDoctor.Git.Cloning;
using FlutterBuildDoctor.Git.Repository;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Composition;

public sealed class GitRefreshCompositionTests
{
    [Fact]
    public void RuntimeDetectionComposition_ResolvesSafeRefreshWorkflowAndDependencies()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFlutterBuildDoctorRuntimeDetection();

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IGitCloneService>());
        Assert.NotNull(provider.GetRequiredService<IGitWorkingTreeScanner>());
        Assert.NotNull(provider.GetRequiredService<IGitRepositoryIdentityService>());
        Assert.NotNull(provider.GetRequiredService<IGitRefreshFileSystem>());
        Assert.NotNull(provider.GetRequiredService<IGitRepositoryRefreshService>());
    }
}
