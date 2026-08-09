using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Application.Services;
using FlutterBuildDoctor.Infrastructure.Environment;
using FlutterBuildDoctor.Infrastructure.Processes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FlutterBuildDoctor.App.DependencyInjection;

public static class RuntimeDetectionServiceCollectionExtensions
{
    public static IServiceCollection AddFlutterBuildDoctorRuntimeDetection(this IServiceCollection services)
    {
        services.TryAddSingleton<IProcessSecretRedactor, DefaultProcessSecretRedactor>();
        services.TryAddSingleton<IProcessRunner, ProcessRunner>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IToolDetector, GitToolDetector>());
        services.TryAddSingleton<IEnvironmentScanner, EnvironmentScanService>();

        return services;
    }
}
