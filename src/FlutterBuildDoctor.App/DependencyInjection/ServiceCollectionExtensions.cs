using FlutterBuildDoctor.App.Services;
using FlutterBuildDoctor.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.App.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFlutterBuildDoctorPresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IGitExecutableResolver, GitExecutableResolver>();
        services.AddSingleton<IRepositoryImportCoordinator, RepositoryImportCoordinator>();
        services.AddSingleton<ProjectHeaderViewModel>();
        services.AddSingleton<RepositoryManagerViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}
