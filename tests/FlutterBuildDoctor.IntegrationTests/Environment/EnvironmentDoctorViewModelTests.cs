using System.IO;
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
    public async Task InitializeAsync_RemainsIdempotentUntilExplicitRefreshIsRequested()
    {
        var service = new StubSnapshotService(BuildSnapshot());
        var viewModel = new EnvironmentDoctorViewModel(service);

        await viewModel.InitializeAsync();
        await viewModel.InitializeAsync();

        Assert.Equal(1, service.CaptureCount);
    }

    [Fact]
    public async Task RefreshCommand_CapturesAndReplacesDashboardWithFreshSnapshot()
    {
        var first = BuildSnapshot(
            flutterVersion: "3.44.8",
            flutterSdkPath: @"C:\flutter",
            completedAt: new DateTimeOffset(2026, 8, 9, 10, 0, 0, TimeSpan.Zero));
        var second = BuildSnapshot(
            flutterVersion: "3.45.1",
            flutterSdkPath: @"D:\flutter-new",
            completedAt: new DateTimeOffset(2026, 8, 9, 11, 0, 0, TimeSpan.Zero));
        var service = new StubSnapshotService(first, second);
        var viewModel = new EnvironmentDoctorViewModel(service);

        await viewModel.InitializeAsync();
        var firstCapturedAt = viewModel.CapturedAtText;

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, service.CaptureCount);
        Assert.True(viewModel.IsLoaded);
        Assert.False(viewModel.IsLoading);
        Assert.NotEqual(firstCapturedAt, viewModel.CapturedAtText);
        var flutter = Assert.Single(viewModel.Components, component => component.Name == "Flutter SDK");
        Assert.Equal(@"D:\flutter-new", flutter.Path);
        Assert.Contains("3.45.1", flutter.Version, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshCommand_WhenCaptureFails_PreservesLastSuccessfulDashboard()
    {
        var initial = BuildSnapshot();
        var service = new RefreshFailingSnapshotService(initial);
        var viewModel = new EnvironmentDoctorViewModel(service);

        await viewModel.InitializeAsync();
        var capturedAt = viewModel.CapturedAtText;
        var flutterBefore = Assert.Single(viewModel.Components, component => component.Name == "Flutter SDK");

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, service.CaptureCount);
        Assert.True(viewModel.IsLoaded);
        Assert.False(viewModel.IsLoading);
        Assert.Equal(14, viewModel.Components.Count);
        Assert.Equal(capturedAt, viewModel.CapturedAtText);
        var flutterAfter = Assert.Single(viewModel.Components, component => component.Name == "Flutter SDK");
        Assert.Equal(flutterBefore.Path, flutterAfter.Path);
        Assert.Equal(flutterBefore.Version, flutterAfter.Version);
        Assert.Contains("refresh failed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("last successful scan remains displayed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
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

    private static EnvironmentSnapshot BuildSnapshot(
        string flutterVersion = "3.44.8",
        string flutterSdkPath = @"C:\flutter",
        DateTimeOffset? completedAt = null)
    {
        var completed = completedAt ?? DateTimeOffset.UtcNow;
        var capturedAt = completed.AddSeconds(-1);
        var variables = new EnvironmentVariableSnapshot(
            capturedAt,
            Variable("PATH", $@"{flutterSdkPath}\bin;C:\tools"),
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
            Path.Combine(flutterSdkPath, "bin", "flutter.bat"),
            flutterSdkPath,
            flutterVersion,
            "stable",
            Array.Empty<FlutterSdkCandidate>(),
            false,
            Message: "Flutter detected.",
            PathDiscovery: pathDiscovery);

        var dart = new DartDetectionResult(
            DartSdkDetectionStatus.Missing,
            flutterSdkPath,
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
            completed,
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

    private sealed class StubSnapshotService(params EnvironmentSnapshot[] snapshots) : IEnvironmentSnapshotService
    {
        private readonly Queue<EnvironmentSnapshot> _snapshots = new(snapshots);

        public int CaptureCount { get; private set; }

        public Task<EnvironmentSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        {
            CaptureCount++;
            if (_snapshots.Count == 0)
            {
                throw new InvalidOperationException("No synthetic snapshots remain.");
            }

            var snapshot = _snapshots.Count > 1 ? _snapshots.Dequeue() : _snapshots.Peek();
            return Task.FromResult(snapshot);
        }
    }

    private sealed class RefreshFailingSnapshotService(EnvironmentSnapshot snapshot) : IEnvironmentSnapshotService
    {
        public int CaptureCount { get; private set; }

        public Task<EnvironmentSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        {
            CaptureCount++;
            return CaptureCount == 1
                ? Task.FromResult(snapshot)
                : throw new InvalidOperationException("Synthetic refresh failure.");
        }
    }

    private sealed class ThrowingSnapshotService : IEnvironmentSnapshotService
    {
        public Task<EnvironmentSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Synthetic capture failure.");
    }
}
