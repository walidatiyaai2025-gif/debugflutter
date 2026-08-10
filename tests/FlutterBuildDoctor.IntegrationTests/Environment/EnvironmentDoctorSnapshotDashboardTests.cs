using FlutterBuildDoctor.Android.Detection;
using FlutterBuildDoctor.App.EnvironmentSnapshots;
using FlutterBuildDoctor.App.ViewModels;
using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Application.Services;
using FlutterBuildDoctor.Domain.Environment;
using FlutterBuildDoctor.Flutter.Detection;

namespace FlutterBuildDoctor.IntegrationTests.Environment;

public sealed class EnvironmentDoctorSnapshotDashboardTests
{
    [Fact]
    public async Task ScanCommand_RefreshesSnapshotDashboardOnEveryRun()
    {
        var ready = CreateReadySnapshot();
        var pendingLicenses = ready with
        {
            AndroidLicenses = new AndroidLicenseDetectionResult(
                AndroidLicenseDetectionStatus.Pending,
                ready.AndroidSdk.EffectiveCandidate?.NormalizedPath,
                ready.AndroidCommandLineTools.EffectiveCandidate?.SdkManagerPath,
                ready.AndroidCommandLineTools.EffectiveCandidate?.Revision,
                new[] { "android-sdk-license" },
                "Accept? (y/N)",
                "One or more Android SDK package licenses require review/acceptance.")
        };

        var snapshotService = new SequencedSnapshotService(ready, pendingLicenses);
        var scanner = new StubEnvironmentScanner();
        var unused = new UnusedDetectorSet();
        using var viewModel = new EnvironmentDoctorViewModel(
            scanner,
            unused,
            unused,
            unused,
            unused,
            unused,
            unused,
            unused,
            unused,
            unused,
            unused,
            environmentSnapshotService: snapshotService);

        await viewModel.ScanCommand.ExecuteAsync(null);

        Assert.Equal("Environment ready", viewModel.OverallReadinessSummary);
        Assert.Equal(13, viewModel.ReadyComponentCount);
        Assert.Equal(0, viewModel.AttentionComponentCount);
        Assert.Equal(13, viewModel.TotalComponentCount);
        Assert.Equal("Accepted / Ready", viewModel.AndroidLicenseSummary);

        await viewModel.ScanCommand.ExecuteAsync(null);

        Assert.Equal("12/13 checks ready", viewModel.OverallReadinessSummary);
        Assert.Equal(12, viewModel.ReadyComponentCount);
        Assert.Equal(1, viewModel.AttentionComponentCount);
        Assert.Equal("Action required", viewModel.AndroidLicenseSummary);
        Assert.Equal(2, snapshotService.CallCount);
        Assert.Equal(2, scanner.CallCount);
    }

    private static EnvironmentSnapshot CreateReadySnapshot()
    {
        var now = DateTimeOffset.UtcNow;
        var variables = new EnvironmentVariableSnapshot(
            now,
            Variable("PATH", @"C:\flutter\bin"),
            Variable("JAVA_HOME", @"C:\Java\jdk-17"),
            Variable("ANDROID_HOME", @"C:\Android\Sdk"),
            Variable("ANDROID_SDK_ROOT", @"C:\Android\Sdk"));

        var windows = new WindowsEnvironmentInfo(
            WindowsEnvironmentDetectionStatus.Succeeded,
            "Microsoft Windows 11 Pro",
            "10.0.26100.0",
            10,
            0,
            26100,
            "X64",
            "X64",
            true,
            true,
            "Windows detected.");

        var studioInstallation = new AndroidStudioInstallation(
            @"C:\Program Files\Android\Android Studio\bin\studio64.exe",
            @"C:\Program Files\Android\Android Studio",
            "Android Studio",
            "2025.1.2",
            "AI-251.12345",
            "AI",
            AndroidStudioDiscoverySource.ProgramFiles,
            AndroidStudioMetadataSource.ProductInfoJson,
            "{}",
            null);
        var studio = new AndroidStudioDetectionResult(
            AndroidStudioDetectionStatus.Succeeded,
            new[] { studioInstallation },
            "Detected 1 Android Studio installation(s).");

        var flutter = new FlutterDetectionResult(
            FlutterSdkDetectionStatus.Succeeded,
            true,
            @"C:\flutter\bin\flutter.bat",
            @"C:\flutter",
            "3.44.8",
            "stable",
            Array.Empty<FlutterSdkCandidate>(),
            false,
            FlutterVersionMetadataSource.None,
            "Flutter ready.");

        var dartCandidate = new DartSdkCandidate(
            @"C:\flutter\bin\cache\dart-sdk\bin\dart.exe",
            @"C:\flutter\bin\cache\dart-sdk",
            "3.12.2",
            true,
            true,
            false,
            @"C:\flutter\bin\cache\dart-sdk\version",
            "3.12.2",
            null);
        var dart = new DartDetectionResult(
            DartSdkDetectionStatus.Succeeded,
            @"C:\flutter",
            dartCandidate,
            dartCandidate,
            new[] { dartCandidate },
            false,
            false,
            "Dart ready.",
            EmptyDiscovery("dart"));

        var javaInstallation = new JavaInstallation(
            @"C:\Java\jdk-17\bin\java.exe",
            @"C:\Java\jdk-17",
            "17.0.12",
            "Temurin",
            "amd64",
            true,
            @"C:\Java\jdk-17\bin\javac.exe",
            0,
            0,
            true,
            false);
        var java = new JavaDetectionResult(
            JavaDetectionStatus.Succeeded,
            javaInstallation,
            new[] { javaInstallation },
            false,
            EmptyDiscovery("java"),
            "Java ready.");

        var sdkCandidate = new AndroidSdkRootCandidate(
            @"C:\Android\Sdk",
            Array.Empty<AndroidSdkRootSourceEvidence>(),
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            null);
        var androidSdk = new AndroidSdkRootDetectionResult(
            AndroidSdkRootDetectionStatus.Succeeded,
            sdkCandidate,
            new[] { sdkCandidate },
            false,
            "Android SDK ready.");

        var commandLineCandidate = new AndroidCommandLineToolsCandidate(
            @"C:\Android\Sdk\cmdline-tools\latest",
            @"C:\Android\Sdk\cmdline-tools\latest\bin\sdkmanager.bat",
            "19.0",
            AndroidCommandLineToolsLayout.LatestAlias,
            true,
            true,
            @"C:\Android\Sdk\cmdline-tools\latest\source.properties",
            "Pkg.Revision=19.0",
            null);
        var commandLineTools = new AndroidCommandLineToolsDetectionResult(
            AndroidCommandLineToolsDetectionStatus.Succeeded,
            @"C:\Android\Sdk",
            commandLineCandidate,
            new[] { commandLineCandidate },
            false,
            "Command-line tools ready.");

        var adb = new AndroidAdbDetectionResult(
            AndroidAdbDetectionStatus.Succeeded,
            @"C:\Android\Sdk",
            @"C:\Android\Sdk\platform-tools",
            @"C:\Android\Sdk\platform-tools\adb.exe",
            "1.0.41",
            "36.0.0-13206524",
            @"C:\Android\Sdk\platform-tools\adb.exe",
            "Android Debug Bridge version 1.0.41",
            "Pkg.Revision=36.0.0",
            "ADB ready.");

        var platform = new AndroidPlatformPackage(
            "android-35",
            @"C:\Android\Sdk\platforms\android-35",
            35,
            "REL",
            "2",
            true,
            true,
            @"C:\Android\Sdk\platforms\android-35\source.properties",
            "Pkg.Revision=2",
            null);
        var platforms = new AndroidPlatformDetectionResult(
            AndroidPlatformDetectionStatus.Succeeded,
            @"C:\Android\Sdk",
            new[] { platform },
            "Platforms ready.");

        var buildToolsPackage = new AndroidBuildToolsPackage(
            "36.0.0",
            @"C:\Android\Sdk\build-tools\36.0.0",
            "36.0.0",
            true,
            true,
            true,
            true,
            @"C:\Android\Sdk\build-tools\36.0.0\source.properties",
            "Pkg.Revision=36.0.0",
            null);
        var buildTools = new AndroidBuildToolsDetectionResult(
            AndroidBuildToolsDetectionStatus.Succeeded,
            @"C:\Android\Sdk",
            new[] { buildToolsPackage },
            "Build tools ready.");

        var emulator = new AndroidEmulatorDetectionResult(
            AndroidEmulatorDetectionStatus.Succeeded,
            @"C:\Android\Sdk",
            @"C:\Android\Sdk\emulator",
            @"C:\Android\Sdk\emulator\emulator.exe",
            "36.1.9.0",
            AndroidEmulatorVersionSource.CommandOutput,
            "Android emulator version 36.1.9.0",
            "Pkg.Revision=36.1.9.0",
            "Emulator ready.");

        var avdCandidate = new AndroidAvdManagerCandidate(
            @"C:\Android\Sdk\cmdline-tools\latest",
            @"C:\Android\Sdk\cmdline-tools\latest\bin\avdmanager.bat",
            "19.0",
            AndroidCommandLineToolsLayout.LatestAlias,
            true,
            true,
            null);
        var avd = new AndroidAvdManagerDetectionResult(
            AndroidAvdManagerDetectionStatus.Succeeded,
            @"C:\Android\Sdk",
            avdCandidate,
            new[] { avdCandidate },
            false,
            "avdmanager ready.");

        var licenses = new AndroidLicenseDetectionResult(
            AndroidLicenseDetectionStatus.Accepted,
            @"C:\Android\Sdk",
            commandLineCandidate.SdkManagerPath,
            commandLineCandidate.Revision,
            new[] { "android-sdk-license" },
            "All SDK package licenses accepted.",
            "All Android SDK package licenses are reported as accepted.");

        return new EnvironmentSnapshot(
            now,
            now.AddMilliseconds(25),
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
            avd,
            licenses,
            studio);
    }

    private static VariableRecord Variable(string name, string value)
        => new(
            name,
            new VariableScopeValue(VariableScope.Process, VariableReadStatus.Present, value),
            new VariableScopeValue(VariableScope.User, VariableReadStatus.Missing, null),
            new VariableScopeValue(VariableScope.Machine, VariableReadStatus.Missing, null));

    private static PathExecutableDiscoveryResult EmptyDiscovery(string executable)
        => new(
            PathExecutableDiscoveryStatus.Succeeded,
            executable,
            Array.Empty<PathExecutableMatch>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<IgnoredPathEntry>(),
            "No conflicts.");

    private sealed class StubEnvironmentScanner : IEnvironmentScanner
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<ToolStatus>> ScanAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<IReadOnlyList<ToolStatus>>(new[]
            {
                new ToolStatus("Git", true, "2.55.0.windows.3", @"C:\Program Files\Git\cmd\git.exe", "Ready")
            });
        }
    }

    private sealed class SequencedSnapshotService : IEnvironmentSnapshotService
    {
        private readonly Queue<EnvironmentSnapshot> _snapshots;

        public SequencedSnapshotService(params EnvironmentSnapshot[] snapshots)
            => _snapshots = new Queue<EnvironmentSnapshot>(snapshots);

        public int CallCount { get; private set; }

        public Task<EnvironmentSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_snapshots.Dequeue());
        }
    }

    private sealed class UnusedDetectorSet :
        IFlutterSdkDetector,
        IJavaInstallationDetector,
        IEnvironmentVariableReader,
        IAndroidSdkRootDetector,
        IAndroidCommandLineToolsDetector,
        IAndroidAdbDetector,
        IAndroidPlatformDetector,
        IAndroidBuildToolsDetector,
        IAndroidEmulatorDetector,
        IAndroidAvdManagerDetector
    {
        private static InvalidOperationException Unexpected()
            => new("Legacy detector path should not be used when IEnvironmentSnapshotService is available.");

        Task<FlutterDetectionResult> IFlutterSdkDetector.DetectAsync(FlutterSdkDetectionRequest? request, CancellationToken cancellationToken)
            => throw Unexpected();

        Task<JavaDetectionResult> IJavaInstallationDetector.DetectAsync(JavaDetectionRequest? request, CancellationToken cancellationToken)
            => throw Unexpected();

        EnvironmentVariableSnapshot IEnvironmentVariableReader.Read() => throw Unexpected();
        AndroidSdkRootDetectionResult IAndroidSdkRootDetector.Detect(EnvironmentVariableSnapshot snapshot) => throw Unexpected();
        AndroidCommandLineToolsDetectionResult IAndroidCommandLineToolsDetector.Detect(AndroidSdkRootDetectionResult sdkRootResult) => throw Unexpected();
        Task<AndroidAdbDetectionResult> IAndroidAdbDetector.DetectAsync(AndroidSdkRootDetectionResult sdkRootResult, CancellationToken cancellationToken) => throw Unexpected();
        AndroidPlatformDetectionResult IAndroidPlatformDetector.Detect(AndroidSdkRootDetectionResult sdkRootResult) => throw Unexpected();
        AndroidBuildToolsDetectionResult IAndroidBuildToolsDetector.Detect(AndroidSdkRootDetectionResult sdkRootResult) => throw Unexpected();
        Task<AndroidEmulatorDetectionResult> IAndroidEmulatorDetector.DetectAsync(AndroidSdkRootDetectionResult sdkRootResult, CancellationToken cancellationToken) => throw Unexpected();
        AndroidAvdManagerDetectionResult IAndroidAvdManagerDetector.Detect(AndroidCommandLineToolsDetectionResult commandLineToolsResult) => throw Unexpected();
    }
}
