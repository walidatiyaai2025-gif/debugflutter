using FlutterBuildDoctor.App.Errors;
using FlutterBuildDoctor.Application.Errors;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.App.DependencyInjection;

public static class ExceptionHandlingServiceCollectionExtensions
{
    public static IServiceCollection AddFlutterBuildDoctorExceptionHandling(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<AppExceptionReporter>();
        services.AddSingleton<IAppExceptionReporter>(provider => provider.GetRequiredService<AppExceptionReporter>());
        services.AddSingleton<GlobalExceptionHooks>();

        return services;
    }
}
