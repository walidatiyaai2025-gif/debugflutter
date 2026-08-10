using FlutterBuildDoctor.Android.Detection;
using FlutterBuildDoctor.Android.Devices;
using FlutterBuildDoctor.Android.Repairs;
using FlutterBuildDoctor.App.EnvironmentSnapshots;
using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Application.Repairs;
using FlutterBuildDoctor.Application.Services;
using FlutterBuildDoctor.Flutter.Build;
using FlutterBuildDoctor.Flutter.Commands;
using FlutterBuildDoctor.Flutter.Detection;
using FlutterBuildDoctor.Flutter.Doctor;
using FlutterBuildDoctor.Flutter.Release;
using FlutterBuildDoctor.Flutter.Repairs;
using FlutterBuildDoctor.Git.Branches;
using FlutterBuildDoctor.Git.Cloning;
using FlutterBuildDoctor.Git.Repository;
using FlutterBuildDoctor.Infrastructure.Environment;
using FlutterBuildDoctor.Infrastructure.Processes;
using FlutterBuildDoctor.Infrastructure.Repairs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FlutterBuildDoctor.App.DependencyInjection;

public static class RuntimeDetectionServiceCollectionExtensions
{
    public static IServiceCollection AddFlutterBuildDoctorRuntimeDetection(this IServiceCollection services)
    {
        services.TryAddSingleton<IProcessSecretRedactor, DefaultProcessSecretRedactor>();
        services.TryAddSingleton<IProcessRunner, ProcessRunner>();
        services.TryAddSingleton<IDetachedProcessLauncher, DetachedProcessLauncher>();
        services.TryAddSingleton<IRepairVerifier, RepairVerifier>();
        services.TryAddSingleton<IProjectPathGuard, ProjectPathGuard>();
        services.TryAddSingleton<IRepairBackupService, FileSystemRepairBackupService>();
        services.TryAddSingleton<IPathExecutableDiscovery, WindowsPathExecutableDiscovery>();
        services.TryAddSingleton<IVariableValueSource, SystemVariableValueSource>();
        services.TryAddSingleton<IEnvironmentVariableReader, EnvironmentVariableReader>();
        services.TryAddSingleton<IWindowsRuntimeInfoSource, SystemWindowsRuntimeInfoSource>();
        services.TryAddSingleton<IWindowsEnvironmentDetector, WindowsEnvironmentDetector>();
        services.TryAddSingleton<IAndroidStudioSearchRootProvider, SystemAndroidStudioSearchRootProvider>();
        services.TryAddSingleton<IAndroidStudioInstallationSource, WindowsAndroidStudioInstallationSource>();
        services.TryAddSingleton<IAndroidStudioDetector, AndroidStudioDetector>();
        services.TryAddSingleton<IFlutterSdkDetector, FlutterSdkDetector>();
        services.TryAddSingleton<IFlutterVersionProbe, FlutterVersionProbe>();
        services.TryAddSingleton<IFlutterDoctorParser, FlutterDoctorParser>();
        services.TryAddSingleton<IFlutterDoctorProbe, FlutterDoctorProbe>();
        services.TryAddSingleton<IFlutterCommandBuilder, FlutterCommandBuilder>();
        services.TryAddSingleton<IFlutterCommandService, FlutterCommandService>();
        services.TryAddSingleton<IFlutterBuildRequestBuilder, FlutterBuildRequestBuilder>();
        services.TryAddSingleton<IBuildArtifactLocator, BuildArtifactLocator>();
        services.TryAddSingleton<IArtifactHashService, Sha256ArtifactHashService>();
        services.TryAddSingleton<IBuildRetryPolicy>(_ => new BuildRetryPolicy(maxRetries: 1));
        services.TryAddSingleton<IFlutterBuildService, FlutterBuildService>();
        services.TryAddSingleton<IAdbDevicesParser, AdbDevicesParser>();
        services.TryAddSingleton<IAvdListParser, AvdListParser>();
        services.TryAddSingleton<IAndroidDeviceMetadataProjector, AndroidDeviceMetadataProjector>();
        services.TryAddSingleton<IAndroidDeviceManager, AndroidDeviceManager>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRepairRecipe, FlutterCleanRepairRecipe>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRepairRecipe, DependencyRefreshRepairRecipe>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRepairRecipe, AdbRestartRepairRecipe>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRepairRecipe, StaleBuildDirectoryCleanupRecipe>());
        services.TryAddSingleton<IReleasePackageInspector, ReleasePackageInspector>();
        services.TryAddSingleton<IReleaseVersionInspector, ReleaseVersionInspector>();
        services.TryAddSingleton<IReleaseSigningInspector, ReleaseSigningInspector>();
        services.TryAddSingleton<IReleaseManifestInspector, ReleaseManifestInspector>();
        services.TryAddSingleton<IReleasePreflightService, ReleasePreflightService>();
        services.TryAddSingleton<IReleaseHistoryStore>(_ => new InMemoryReleaseHistoryStore(capacity: 100));
        services.TryAddSingleton<IReleaseOutputActionService, ReleaseOutputActionService>();
        services.TryAddSingleton<IReleaseOrchestrator, ReleaseOrchestrator>();
        services.TryAddSingleton<IDartSdkDetector, DartSdkDetector>();
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
