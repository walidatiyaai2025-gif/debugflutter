using FlutterBuildDoctor.Android.Detection;
using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Application.Services;
using FlutterBuildDoctor.Flutter.Detection;
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
        services.TryAddSingleton<IVariableValueSource, SystemVariableValueSource>();
        services.TryAddSingleton<IEnvironmentVariableReader, EnvironmentVariableReader>();
        services.TryAddSingleton<IWindowsRuntimeInfoSource, SystemWindowsRuntimeInfoSource>();
        services.TryAddSingleton<IWindowsEnvironmentDetector, WindowsEnvironmentDetector>();
        services.TryAddSingleton<IFlutterSdkDetector, FlutterSdkDetector>();
        services.TryAddSingleton<IJavaInstallationDetector, JavaInstallationDetector>();
        services.TryAddSingleton<IAndroidSdkRootDetector, AndroidSdkRootDetector>();
        services.TryAddSingleton<IAndroidCommandLineToolsDetector, AndroidCommandLineToolsDetector>();
        services.TryAddSingleton<IAndroidAdbDetector, AndroidAdbDetector>();
        services.TryAddSingleton<IAndroidPlatformDetector, AndroidPlatformDetector>();
        services.TryAddSingleton<IAndroidBuildToolsDetector, AndroidBuildToolsDetector>();
        services.TryAddSingleton<IAndroidEmulatorDetector, AndroidEmulatorDetector>();
        services.TryAddSingleton<IAndroidAvdManagerDetector, AndroidAvdManagerDetector>();
        services.TryAddSingleton<IAndroidLicenseDetector, AndroidLicenseDetector>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IToolDetector, GitToolDetector>());
        services.TryAddSingleton<IEnvironmentScanner, EnvironmentScanService>();
        services.TryAddSingleton<IGitCloneService, GitCloneService>();
        services.TryAddSingleton<IGitBranchService, GitBranchService>();
        services.TryAddSingleton<IGitBranchSwitcher, GitBranchSwitcher>();
        services.TryAddSingleton<IGitPullService, GitPullService>();
        services.TryAddSingleton<IGitWorkingTreeScanner, GitWorkingTreeScanner>();
        services.TryAddSingleton<IGitRepositoryIdentityService, GitRepositoryIdentityService>();
        services.TryAddSingleton<IGitWorkspaceLockResolver, WindowsRestartManagerWorkspaceLockResolver>();
        services.TryAddSingleton<IGitRefreshFileSystem>(serviceProvider =>
            new LockRecoveringGitRefreshFileSystem(new GitRefreshFileSystem(), serviceProvider.GetRequiredService<IGitWorkspaceLockResolver>()));
        services.TryAddSingleton<IGitRepositoryRefreshService, GitRepositoryRefreshService>();
        return services;
    }
}
