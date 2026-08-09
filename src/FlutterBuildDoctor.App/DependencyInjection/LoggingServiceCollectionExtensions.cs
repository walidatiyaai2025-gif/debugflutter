using FlutterBuildDoctor.Application.Logging;
using FlutterBuildDoctor.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.App.DependencyInjection;

public static class LoggingServiceCollectionExtensions
{
    public static IServiceCollection AddFlutterBuildDoctorLogging(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<InMemoryAppLogStore>();
        services.AddSingleton<IAppLogStore>(provider => provider.GetRequiredService<InMemoryAppLogStore>());
        services.AddSingleton<AppLogStoreSink>();

        return services;
    }
}
