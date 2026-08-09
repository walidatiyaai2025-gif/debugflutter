using FlutterBuildDoctor.App;
using FlutterBuildDoctor.App.DependencyInjection;
using FlutterBuildDoctor.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Composition;

public sealed class PresentationCompositionTests
{
    [Fact]
    public void PresentationComposition_ResolvesShellViewModelAndWindowRegistration()
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
        Assert.NotNull(viewModel.ProjectHeader);
        Assert.Equal("No project selected", viewModel.ProjectHeader.ProjectName);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(ProjectHeaderViewModel)
                && descriptor.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(MainWindow)
                && descriptor.Lifetime == ServiceLifetime.Singleton);
    }
}
