using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Application.Services;
using FlutterBuildDoctor.Git.Branches;
using FlutterBuildDoctor.Git.Cloning;
using FlutterBuildDoctor.Git.Repository;
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
        services.TryAddSingleton<IPathExecutableDiscovery, WindowsPathExecutableDiscovery>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IToolDetector, GitToolDetector>());
        services.TryAddSingleton<IEnvironmentScanner, EnvironmentScanService>();
        services.TryAddSingleton<IGitCloneService, GitCloneService>();
        services.TryAddSingleton<IGitBranchService, GitBranchService>();
        services.TryAddSingleton<IGitBranchSwitcher, GitBranchSwitcher>();
        services.TryAddSingleton<IGitWorkingTreeScanner, GitWorkingTreeScanner>();
        services.TryAddSingleton<IGitRepositoryIdentityService, GitRepositoryIdentityService>();
        services.TryAddSingleton<IGitRefreshFileSystem, GitRefreshFileSystem>();
        services.TryAddSingleton<IGitRepositoryRefreshService, GitRepositoryRefreshService>();

        return services;
    }
}
