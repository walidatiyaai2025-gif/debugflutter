using FlutterBuildDoctor.App;
using FlutterBuildDoctor.App.DependencyInjection;
using FlutterBuildDoctor.App.Services;
using FlutterBuildDoctor.App.ViewModels;
using FlutterBuildDoctor.Git.Branches;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Composition;

public sealed class PresentationCompositionTests
{
    [Fact]
    public void PresentationComposition_ResolvesShellRepositoryManagerEnvironmentDoctorAndWindowRegistration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFlutterBuildDoctorExceptionHandling();
        services.AddFlutterBuildDoctorRuntimeDetection();
        services.AddFlutterBuildDoctorPresentation();

        using var provider = services.BuildServiceProvider();
        var viewModel = provider.GetRequiredService<MainWindowViewModel>();

        Assert.Equal("Flutter Build Doctor", viewModel.ApplicationName);
        Assert.Equal("Ready", viewModel.StartupStatus);
        Assert.Equal("Ready", viewModel.StatusMessage);
        Assert.True(viewModel.IsDashboardVisible);
        Assert.False(viewModel.IsProjectsVisible);
        Assert.False(viewModel.IsEnvironmentDoctorVisible);
        Assert.NotNull(viewModel.ProjectHeader);
        Assert.Equal("No project selected", viewModel.ProjectHeader.ProjectName);
        Assert.NotNull(viewModel.RepositoryManager);
        Assert.Same(viewModel.RepositoryManager, provider.GetRequiredService<RepositoryManagerViewModel>());
        Assert.NotNull(viewModel.EnvironmentDoctor);
        Assert.Same(viewModel.EnvironmentDoctor, provider.GetRequiredService<EnvironmentDoctorViewModel>());
        Assert.NotNull(provider.GetRequiredService<IGitExecutableResolver>());
        Assert.NotNull(provider.GetRequiredService<IRepositoryImportCoordinator>());
        Assert.NotNull(provider.GetRequiredService<IGitBranchService>());
        Assert.NotNull(provider.GetRequiredService<IGitBranchSwitcher>());
        Assert.NotNull(provider.GetRequiredService<IGitPullService>());
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(ProjectHeaderViewModel)
                && descriptor.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(RepositoryManagerViewModel)
                && descriptor.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(EnvironmentDoctorViewModel)
                && descriptor.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(MainWindow)
                && descriptor.Lifetime == ServiceLifetime.Singleton);
    }
}
