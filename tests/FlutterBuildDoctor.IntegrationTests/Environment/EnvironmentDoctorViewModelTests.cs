using FlutterBuildDoctor.Android.Detection;
using FlutterBuildDoctor.App.EnvironmentSnapshots;
using FlutterBuildDoctor.App.ViewModels;
using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Flutter.Detection;

namespace FlutterBuildDoctor.IntegrationTests.Environment;

public sealed class EnvironmentDoctorViewModelTests
{
    [Fact]
    public async Task InitializeAsync_MapsSnapshotIntoStatePathVersionAndActionCards()
    {
        var service = new StubSnapshotService(BuildSnapshot());
        var viewModel = new EnvironmentDoctorViewModel(service);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsLoaded);
        Assert.False(viewModel.IsLoading);
        Assert.Equal(14, viewModel.Components.Count);
        Assert.Equal(3, viewModel.ReadyCount);
        Assert.Equal(11, viewModel.AttentionCount);
        Assert.Contains("3 ready, 11 need attention", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);

        var flutter = Assert.Single(viewModel.Components, component => component.Name == "Flutter SDK");
        Assert.True(flutter.IsReady);
        Assert.Equal(@"C:\flutter", flutter.Path);
        Assert.Contains("3.44.8", flutter.Version, StringComparison.Ordinal);
        Assert.Contains("stable", flutter.Version, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("No action required.", flutter.Action);

        var java = Assert.Single(viewModel.Components, component => component.Name == "Java / JDK");
        Assert.True(java.NeedsAttention);
        Assert.Equal("Not detected", java.Path);
        Assert.Contains("JAVA_HOME", java.Action, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotentUntilRefreshTaskIsImplemented()
    {
        var service = new StubSnapshotService(BuildSnapshot());
        var viewModel = new EnvironmentDoctorViewModel(service);

        await viewModel.InitializeAsync();
        await viewModel.InitializeAsync();

        Assert.Equal(1, service.CaptureCount);
    }

    [Fact]
    public async Task InitializeAsync_WhenCaptureFails_SurfacesFailureWithoutClaimingLoadedState()
    {
        var viewModel = new EnvironmentDoctorViewModel(new ThrowingSnapshotService());

        await viewModel.InitializeAsync();

        Assert.False(viewModel.IsLoaded);
        Assert.False(viewModel.IsLoading);
        Assert.Empty(viewModel.Components);
        Assert.Equal(0, viewModel.ReadyCount);
        Assert.Equal(0, viewModel.AttentionCount);
        Assert.Contains("scan failed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static EnvironmentSnapshot BuildSnapshot()
    {
        var capturedAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        var completedAt = DateTimeOffset.UtcNow;
        var variables = new EnvironmentVariableSnapshot(
            capturedAt,
            Variable("PATH", @"C:\flutter\bin;C:\tools"),
            Variable("JAVA_HOME", null),
            Variable("ANDROID_HOME", null),
            Variable("ANDROID_SDK_ROOT", null));
        var pathDiscovery = EmptyPathDiscovery("tool");

        var windows = new WindowsEnvironmentInfo(
            WindowsEnvironmentDetectionStatus.Succeeded,
            "Windows test",
            "10.0.26100.0",
            10,
            0,
            26100,
            "X64",
            "X64",
            true,
            true,
            "Windows detected.");

        var flutter = new FlutterDetectionResult(
            FlutterSdkDetectionStatus.Succeeded,
            true,
            @"C:\flutter\bin\flutter.bat",
            @"C:\flutter",
            "3.44.8",
            "stable",
            Array.Empty<FlutterSdkCandidate>(),
            false,
            Message: "Flutter detected.",
            PathDiscovery: pathDiscovery);

        var dart = new DartDetectionResult(
            DartSdkDetectionStatus.Missing,
            @"C:\flutter",
            null,
            null,
            Array.Empty<DartSdkCandidate>(),
            false,
            false,
            "Dart missing.",
            pathDiscovery);

        var java = new JavaDetectionResult(
            JavaDetectionStatus.Missing,
            null,
            Array.Empty<JavaInstallation>(),
            false,
            pathDiscovery,
            "Java missing.");

        var androidSdk = new AndroidSdkRootDetectionResult(
            AndroidSdkRootDetectionStatus.MissingEffectiveRoot,
            null,
            Array.Empty<AndroidSdkRootCandidate>(),
            false,
            "Android SDK missing.");

        var commandLineTools = new AndroidCommandLineToolsDetectionResult(
            AndroidCommandLineToolsDetectionStatus.AndroidSdkRootUnavailable,
            string.Empty,
            null,
            Array.Empty<AndroidCommandLineToolsCandidate>(),
            false,
            "Command-line tools unavailable.");

        var adb = new AndroidAdbDetectionResult(
            AndroidAdbDetectionStatus.AndroidSdkRootUnavailable,
            string.Empty,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            "ADB unavailable.");

        var platforms = new AndroidPlatformDetectionResult(
            AndroidPlatformDetectionStatus.AndroidSdkRootUnavailable,
            string.Empty,
            Array.Empty<AndroidPlatformPackage>(),
            "Platforms unavailable.");

        var buildTools = new AndroidBuildToolsDetectionResult(
            AndroidBuildToolsDetectionStatus.AndroidSdkRootUnavailable,
            string.Empty,
            Array.Empty<AndroidBuildToolsPackage>(),
            "Build tools unavailable.");

        var emulator = new AndroidEmulatorDetectionResult(
            AndroidEmulatorDetectionStatus.AndroidSdkRootUnavailable,
            string.Empty,
            null,
            null,
            null,
            AndroidEmulatorVersionSource.None,
            null,
            null,
            "Emulator unavailable.");

        var avdManager = new AndroidAvdManagerDetectionResult(
            AndroidAvdManagerDetectionStatus.CommandLineToolsUnavailable,
            string.Empty,
            null,
            Array.Empty<AndroidAvdManagerCandidate>(),
            false,
            "AVD manager unavailable.");

        var licenses = new AndroidLicenseDetectionResult(
            AndroidLicenseDetectionStatus.SdkManagerUnavailable,
            string.Empty,
            null,
            null,
            Array.Empty<string>(),
            null,
            "Licenses unavailable.");

        var androidStudio = new AndroidStudioDetectionResult(
            AndroidStudioDetectionStatus.Missing,
            Array.Empty<AndroidStudioInstallation>(),
            "Android Studio missing.");

        return new EnvironmentSnapshot(
            capturedAt,
            completedAt,
            windows,
            variables,
            flutter,
            dart,
            java,
            androidSdk,
            commandLineTools,
            adb,
            platforms,
            buildTools,
            emulator,
            avdManager,
            licenses,
            androidStudio);
    }

    private static VariableRecord Variable(string name, string? processValue)
        => new(
            name,
            Scope(VariableScope.Process, processValue),
            Scope(VariableScope.User, null),
            Scope(VariableScope.Machine, null));

    private static VariableScopeValue Scope(VariableScope scope, string? value)
        => new(
            scope,
            value is null ? VariableReadStatus.Missing : VariableReadStatus.Present,
            value);

    private static PathExecutableDiscoveryResult EmptyPathDiscovery(string executableName)
        => new(
            PathExecutableDiscoveryStatus.Succeeded,
            executableName,
            Array.Empty<PathExecutableMatch>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<IgnoredPathEntry>(),
            "No matches.");

    private sealed class StubSnapshotService(EnvironmentSnapshot snapshot) : IEnvironmentSnapshotService
    {
        public int CaptureCount { get; private set; }

        public Task<EnvironmentSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        {
            CaptureCount++;
            return Task.FromResult(snapshot);
        }
    }

    private sealed class ThrowingSnapshotService : IEnvironmentSnapshotService
    {
        public Task<EnvironmentSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Synthetic capture failure.");
    }
}
