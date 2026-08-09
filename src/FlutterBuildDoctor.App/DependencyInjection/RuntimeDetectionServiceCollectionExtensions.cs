using FlutterBuildDoctor.Android.Detection;
using FlutterBuildDoctor.App.EnvironmentSnapshots;
using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Application.Services;
using FlutterBuildDoctor.Flutter.Detection;
using FlutterBuildDoctor.Flutter.Doctor;
using FlutterBuildDoctor.Flutter.ProjectAnalysis;
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
        services.TryAddSingleton<IAndroidStudioSearchRootProvider, SystemAndroidStudioSearchRootProvider>();
        services.TryAddSingleton<IAndroidStudioInstallationSource, WindowsAndroidStudioInstallationSource>();
        services.TryAddSingleton<IAndroidStudioDetector, AndroidStudioDetector>();
        services.TryAddSingleton<IFlutterSdkDetector, FlutterSdkDetector>();
        services.TryAddSingleton<IDartSdkDetector, DartSdkDetector>();
        services.TryAddSingleton<IFlutterVersionProbe, FlutterVersionProbe>();
        services.TryAddSingleton<IFlutterDoctorExecutor, FlutterDoctorExecutor>();
        services.TryAddSingleton<IFlutterDoctorParser, FlutterDoctorParser>();
        services.TryAddSingleton<IFlutterProjectRootLocator, FlutterProjectRootLocator>();
        services.TryAddSingleton<IPubspecParser, PubspecParser>();
        services.TryAddSingleton<IPubspecLockParser, PubspecLockParser>();
        services.TryAddSingleton<IGradleDslDetector, GradleDslDetector>();
        services.TryAddSingleton<IGradleWrapperVersionParser, GradleWrapperVersionParser>();
        services.TryAddSingleton<IAndroidGradlePluginVersionParser, AndroidGradlePluginVersionParser>();
        services.TryAddSingleton<IKotlinPluginVersionParser, KotlinPluginVersionParser>();
        services.TryAddSingleton<IJavaInstallationDetector, JavaInstallationDetector>();
        services.TryAddSingleton<IAndroidSdkRootDetector, AndroidSdkRootDetector>();
        services.TryAddSingleton<IAndroidCommandLineToolsDetector, AndroidCommandLineToolsDetector>();
        services.TryAddSingleton<IAndroidAdbDetector, AndroidAdbDetector>();
        services.TryAddSingleton<IAndroidPlatformDetector, AndroidPlatformDetector>();
        services.TryAddSingleton<IAndroidBuildToolsDetector, AndroidBuildToolsDetector>();
        services.TryAddSingleton<IAndroidEmulatorDetector, AndroidEmulatorDetector>();
        services.TryAddSingleton<IAndroidAvdManagerDetector, AndroidAvdManagerDetector>();
        services.TryAddSingleton<IAndroidLicenseDetector, AndroidLicenseDetector>();
        services.TryAddSingleton<IEnvironmentSnapshotService, EnvironmentSnapshotService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IToolDetector, GitToolDetector>());
        services.TryAddSingleton<IEnvironmentScanner, EnvironmentScanService>();
        services.TryAddSingleton<IGitCloneService, GitCloneService>();
        services.TryAddSingleton<IGitBranchService, GitBranchService>();
        services.TryAddSingleton<IGitBranchSwitcher, GitBranchSwitcher>();
        services.TryAddSingleton<IGitWorkingTreeScanner, GitWorkingTreeScanner>();
        services.TryAddSingleton<IGitRepositoryIdentityService, GitRepositoryIdentityService>();
        services.TryAddSingleton<IGitWorkspaceLockResolver, WindowsRestartManagerWorkspaceLockResolver>();
        services.TryAddSingleton<IGitRefreshFileSystem>(serviceProvider =>
            new LockRecoveringGitRefreshFileSystem(
                new GitRefreshFileSystem(),
                serviceProvider.GetRequiredService<IGitWorkspaceLockResolver>()));
        services.TryAddSingleton<IGitRepositoryRefreshService, GitRepositoryRefreshService>();

        return services;
    }
}