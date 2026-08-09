using FlutterBuildDoctor.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.App.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFlutterBuildDoctorPresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ProjectHeaderViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}
