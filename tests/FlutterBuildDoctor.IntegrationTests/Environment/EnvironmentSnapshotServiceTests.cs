using FlutterBuildDoctor.Android.Detection;
using FlutterBuildDoctor.App.EnvironmentSnapshots;
using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Flutter.Detection;

namespace FlutterBuildDoctor.IntegrationTests.Environment;

public sealed class EnvironmentSnapshotServiceTests
{
    [Fact]
    public async Task CaptureAsync_ComposesOneConsistentSnapshotFromExistingDetectors()
    {
        var stubs = new StubDetectorSet();
        var service = new EnvironmentSnapshotService(
            stubs,
            stubs,
            stubs,
            stubs,
            stubs,
            stubs,
            stubs,
            stubs,
            stubs,
            stubs,
            stubs,
            stubs,
            stubs,
            stubs);

        var snapshot = await service.CaptureAsync();

        Assert.Same(stubs.Windows, snapshot.Windows);
        Assert.Same(stubs.EnvironmentVariables, snapshot.EnvironmentVariables);
        Assert.Same(stubs.Flutter, snapshot.Flutter);
        Assert.Same(stubs.Dart, snapshot.Dart);
        Assert.Same(stubs.Java, snapshot.Java);
        Assert.Same(stubs.AndroidSdk, snapshot.AndroidSdk);
        Assert.Same(stubs.CommandLineTools, snapshot.AndroidCommandLineTools);
        Assert.Same(stubs.Adb, snapshot.Adb);
        Assert.Same(stubs.Platforms, snapshot.AndroidPlatforms);
        Assert.Same(stubs.BuildTools, snapshot.AndroidBuildTools);
        Assert.Same(stubs.Emulator, snapshot.Emulator);
        Assert.Same(stubs.AvdManager, snapshot.AvdManager);
        Assert.Same(stubs.Licenses, snapshot.AndroidLicenses);
        Assert.Same(stubs.AndroidStudio, snapshot.AndroidStudio);
        Assert.True(snapshot.CompletedAt >= snapshot.CapturedAt);
        Assert.True(snapshot.CaptureDuration >= TimeSpan.Zero);

        Assert.Equal(stubs.EnvironmentVariables.Path.EffectiveValue, stubs.FlutterPathValue);
        Assert.Equal(stubs.EnvironmentVariables.Path.EffectiveValue, stubs.DartPathValue);
        Assert.Equal(stubs.EnvironmentVariables.Path.EffectiveValue, stubs.JavaPathValue);
        Assert.Same(stubs.Flutter, stubs.DartFlutterInput);
        Assert.Same(stubs.EnvironmentVariables, stubs.AndroidSdkEnvironmentInput);
        Assert.Same(stubs.AndroidSdk, stubs.CommandLineToolsSdkInput);
        Assert.Same(stubs.AndroidSdk, stubs.AdbSdkInput);
        Assert.Same(stubs.AndroidSdk, stubs.PlatformSdkInput);
        Assert.Same(stubs.AndroidSdk, stubs.BuildToolsSdkInput);
        Assert.Same(stubs.AndroidSdk, stubs.EmulatorSdkInput);
        Assert.Same(stubs.CommandLineTools, stubs.AvdCommandLineToolsInput);
        Assert.Same(stubs.CommandLineTools, stubs.LicenseCommandLineToolsInput);
        Assert.Same(stubs.Windows, stubs.AndroidStudioWindowsInput);
    }

    private sealed class StubDetectorSet :
        IWindowsEnvironmentDetector,
        IEnvironmentVariableReader,
        IFlutterSdkDetector,
        IDartSdkDetector,
        IJavaInstallationDetector,
        IAndroidSdkRootDetector,
        IAndroidCommandLineToolsDetector,
        IAndroidAdbDetector,
        IAndroidPlatformDetector,
        IAndroidBuildToolsDetector,
        IAndroidEmulatorDetector,
        IAndroidAvdManagerDetector,
        IAndroidLicenseDetector,
        IAndroidStudioDetector
    {
        public StubDetectorSet()
        {
            Windows = new WindowsEnvironmentInfo(
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

            EnvironmentVariables = new EnvironmentVariableSnapshot(
                DateTimeOffset.UtcNow,
                Variable("PATH", @"C:\tools"),
                Variable("JAVA_HOME", null),
                Variable("ANDROID_HOME", null),
                Variable("ANDROID_SDK_ROOT", null));

            var pathDiscovery = EmptyPathDiscovery("tool");
            Flutter = new FlutterDetectionResult(
                FlutterSdkDetectionStatus.Missing,
                false,
                null,
                null,
                null,
                null,
                Array.Empty<FlutterSdkCandidate>(),
                false,
                Message: "Flutter missing.",
                PathDiscovery: pathDiscovery);

            Dart = new DartDetectionResult(
                DartSdkDetectionStatus.Missing,
                null,
                null,
                null,
                Array.Empty<DartSdkCandidate>(),
                false,
                false,
                "Dart missing.",
                pathDiscovery);

            Java = new JavaDetectionResult(
                JavaDetectionStatus.Missing,
                null,
                Array.Empty<JavaInstallation>(),
                false,
                pathDiscovery,
                "Java missing.");

            AndroidSdk = new AndroidSdkRootDetectionResult(
                AndroidSdkRootDetectionStatus.MissingEffectiveRoot,
                null,
                Array.Empty<AndroidSdkRootCandidate>(),
                false,
                "Android SDK missing.");

            CommandLineTools = new AndroidCommandLineToolsDetectionResult(
                AndroidCommandLineToolsDetectionStatus.AndroidSdkRootUnavailable,
                string.Empty,
                null,
                Array.Empty<AndroidCommandLineToolsCandidate>(),
                false,
                "Command-line tools unavailable.");

            Adb = new AndroidAdbDetectionResult(
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

            Platforms = new AndroidPlatformDetectionResult(
                AndroidPlatformDetectionStatus.AndroidSdkRootUnavailable,
                string.Empty,
                Array.Empty<AndroidPlatformPackage>(),
                "Platforms unavailable.");

            BuildTools = new AndroidBuildToolsDetectionResult(
                AndroidBuildToolsDetectionStatus.AndroidSdkRootUnavailable,
                string.Empty,
                Array.Empty<AndroidBuildToolsPackage>(),
                "Build tools unavailable.");

            Emulator = new AndroidEmulatorDetectionResult(
                AndroidEmulatorDetectionStatus.AndroidSdkRootUnavailable,
                string.Empty,
                null,
                null,
                null,
                AndroidEmulatorVersionSource.None,
                null,
                null,
                "Emulator unavailable.");

            AvdManager = new AndroidAvdManagerDetectionResult(
                AndroidAvdManagerDetectionStatus.CommandLineToolsUnavailable,
                string.Empty,
                null,
                Array.Empty<AndroidAvdManagerCandidate>(),
                false,
                "AVD manager unavailable.");

            Licenses = new AndroidLicenseDetectionResult(
                AndroidLicenseDetectionStatus.SdkManagerUnavailable,
                string.Empty,
                null,
                null,
                Array.Empty<string>(),
                null,
                "Licenses unavailable.");

            AndroidStudio = new AndroidStudioDetectionResult(
                AndroidStudioDetectionStatus.Missing,
                Array.Empty<AndroidStudioInstallation>(),
                "Android Studio missing.");
        }

        public WindowsEnvironmentInfo Windows { get; }
        public EnvironmentVariableSnapshot EnvironmentVariables { get; }
        public FlutterDetectionResult Flutter { get; }
        public DartDetectionResult Dart { get; }
        public JavaDetectionResult Java { get; }
        public AndroidSdkRootDetectionResult AndroidSdk { get; }
        public AndroidCommandLineToolsDetectionResult CommandLineTools { get; }
        public AndroidAdbDetectionResult Adb { get; }
        public AndroidPlatformDetectionResult Platforms { get; }
        public AndroidBuildToolsDetectionResult BuildTools { get; }
        public AndroidEmulatorDetectionResult Emulator { get; }
        public AndroidAvdManagerDetectionResult AvdManager { get; }
        public AndroidLicenseDetectionResult Licenses { get; }
        public AndroidStudioDetectionResult AndroidStudio { get; }

        public string? FlutterPathValue { get; private set; }
        public string? DartPathValue { get; private set; }
        public string? JavaPathValue { get; private set; }
        public FlutterDetectionResult? DartFlutterInput { get; private set; }
        public EnvironmentVariableSnapshot? AndroidSdkEnvironmentInput { get; private set; }
        public AndroidSdkRootDetectionResult? CommandLineToolsSdkInput { get; private set; }
        public AndroidSdkRootDetectionResult? AdbSdkInput { get; private set; }
        public AndroidSdkRootDetectionResult? PlatformSdkInput { get; private set; }
        public AndroidSdkRootDetectionResult? BuildToolsSdkInput { get; private set; }
        public AndroidSdkRootDetectionResult? EmulatorSdkInput { get; private set; }
        public AndroidCommandLineToolsDetectionResult? AvdCommandLineToolsInput { get; private set; }
        public AndroidCommandLineToolsDetectionResult? LicenseCommandLineToolsInput { get; private set; }
        public WindowsEnvironmentInfo? AndroidStudioWindowsInput { get; private set; }

        WindowsEnvironmentInfo IWindowsEnvironmentDetector.Detect() => Windows;

        EnvironmentVariableSnapshot IEnvironmentVariableReader.Read() => EnvironmentVariables;

        Task<FlutterDetectionResult> IFlutterSdkDetector.DetectAsync(
            FlutterSdkDetectionRequest? request,
            CancellationToken cancellationToken)
        {
            FlutterPathValue = request?.PathValue;
            return Task.FromResult(Flutter);
        }

        Task<DartDetectionResult> IDartSdkDetector.DetectAsync(
            FlutterDetectionResult flutterResult,
            DartSdkDetectionRequest? request,
            CancellationToken cancellationToken)
        {
            DartFlutterInput = flutterResult;
            DartPathValue = request?.PathValue;
            return Task.FromResult(Dart);
        }

        Task<JavaDetectionResult> IJavaInstallationDetector.DetectAsync(
            JavaDetectionRequest? request,
            CancellationToken cancellationToken)
        {
            JavaPathValue = request?.PathValue;
            return Task.FromResult(Java);
        }

        AndroidSdkRootDetectionResult IAndroidSdkRootDetector.Detect(EnvironmentVariableSnapshot snapshot)
        {
            AndroidSdkEnvironmentInput = snapshot;
            return AndroidSdk;
        }

        AndroidCommandLineToolsDetectionResult IAndroidCommandLineToolsDetector.Detect(AndroidSdkRootDetectionResult sdkRootResult)
        {
            CommandLineToolsSdkInput = sdkRootResult;
            return CommandLineTools;
        }

        Task<AndroidAdbDetectionResult> IAndroidAdbDetector.DetectAsync(
            AndroidSdkRootDetectionResult sdkRootResult,
            CancellationToken cancellationToken)
        {
            AdbSdkInput = sdkRootResult;
            return Task.FromResult(Adb);
        }

        AndroidPlatformDetectionResult IAndroidPlatformDetector.Detect(AndroidSdkRootDetectionResult sdkRootResult)
        {
            PlatformSdkInput = sdkRootResult;
            return Platforms;
        }

        AndroidBuildToolsDetectionResult IAndroidBuildToolsDetector.Detect(AndroidSdkRootDetectionResult sdkRootResult)
        {
            BuildToolsSdkInput = sdkRootResult;
            return BuildTools;
        }

        Task<AndroidEmulatorDetectionResult> IAndroidEmulatorDetector.DetectAsync(
            AndroidSdkRootDetectionResult sdkRootResult,
            CancellationToken cancellationToken)
        {
            EmulatorSdkInput = sdkRootResult;
            return Task.FromResult(Emulator);
        }

        AndroidAvdManagerDetectionResult IAndroidAvdManagerDetector.Detect(AndroidCommandLineToolsDetectionResult commandLineToolsResult)
        {
            AvdCommandLineToolsInput = commandLineToolsResult;
            return AvdManager;
        }

        Task<AndroidLicenseDetectionResult> IAndroidLicenseDetector.DetectAsync(
            AndroidCommandLineToolsDetectionResult commandLineToolsResult,
            CancellationToken cancellationToken)
        {
            LicenseCommandLineToolsInput = commandLineToolsResult;
            return Task.FromResult(Licenses);
        }

        AndroidStudioDetectionResult IAndroidStudioDetector.Detect(WindowsEnvironmentInfo windowsEnvironment)
        {
            AndroidStudioWindowsInput = windowsEnvironment;
            return AndroidStudio;
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
    }
}
