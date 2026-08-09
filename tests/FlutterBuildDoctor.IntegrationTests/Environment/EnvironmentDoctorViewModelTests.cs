using FlutterBuildDoctor.Android.Detection;
using FlutterBuildDoctor.App.ViewModels;
using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Application.Services;
using FlutterBuildDoctor.Domain.Environment;
using FlutterBuildDoctor.Flutter.Detection;

namespace FlutterBuildDoctor.IntegrationTests.Environment;

public sealed class EnvironmentDoctorViewModelTests
{
    [Fact]
    public async Task ScanCommand_ProjectsReadyBackendDetectorsIntoUiState()
    {
        var snapshot = CreateEnvironmentSnapshot();
        var sdkCandidate = new AndroidSdkRootCandidate(
            "C:\\Android\\Sdk",
            new[]
            {
                new AndroidSdkRootSourceEvidence(
                    "ANDROID_SDK_ROOT",
                    VariableScope.Process,
                    "C:\\Android\\Sdk")
            },
            IsEffective: true,
            Exists: true,
            HasRecognizedSdkLayout: true,
            HasPlatformToolsDirectory: true,
            HasPlatformsDirectory: true,
            HasBuildToolsDirectory: true,
            HasCmdlineToolsDirectory: true,
            HasLicensesDirectory: true,
            ValidationMessage: "valid");
        var sdkResult = new AndroidSdkRootDetectionResult(
            AndroidSdkRootDetectionStatus.Succeeded,
            sdkCandidate,
            new[] { sdkCandidate },
            HasConflict: false,
            Message: "Android SDK root is valid.");
        var commandLineCandidate = new AndroidCommandLineToolsCandidate(
            "C:\\Android\\Sdk\\cmdline-tools\\latest",
            "C:\\Android\\Sdk\\cmdline-tools\\latest\\bin\\sdkmanager.bat",
            "19.0",
            AndroidCommandLineToolsLayout.LatestAlias,
            IsEffective: true,
            SdkManagerExists: true,
            SourcePropertiesPath: "C:\\Android\\Sdk\\cmdline-tools\\latest\\source.properties",
            RawSourceProperties: "Pkg.Revision=19.0",
            Message: null);
        var adbResult = new AndroidAdbDetectionResult(
            AndroidAdbDetectionStatus.Succeeded,
            "C:\\Android\\Sdk",
            "C:\\Android\\Sdk\\platform-tools",
            "C:\\Android\\Sdk\\platform-tools\\adb.exe",
            "1.0.41",
            "36.0.0-13206524",
            "C:\\Android\\Sdk\\platform-tools\\adb.exe",
            "Android Debug Bridge version 1.0.41",
            "Pkg.Revision=36.0.0",
            "ADB 1.0.41 / platform-tools 36.0.0-13206524 detected.");
        var platformPackage = new AndroidPlatformPackage(
            "android-35",
            "C:\\Android\\Sdk\\platforms\\android-35",
            ApiLevel: 35,
            CodeName: "REL",
            Revision: "2",
            AndroidJarExists: true,
            FrameworkAidlExists: true,
            SourcePropertiesPath: "C:\\Android\\Sdk\\platforms\\android-35\\source.properties",
            RawSourceProperties: "Pkg.Revision=2\nAndroidVersion.ApiLevel=35\nAndroidVersion.CodeName=REL\n",
            Message: null);
        var platformResult = new AndroidPlatformDetectionResult(
            AndroidPlatformDetectionStatus.Succeeded,
            "C:\\Android\\Sdk",
            new[] { platformPackage },
            "Detected 1 usable Android platform package(s).");
        var buildToolsPackage = new AndroidBuildToolsPackage(
            "36.0.0",
            "C:\\Android\\Sdk\\build-tools\\36.0.0",
            Revision: "36.0.0",
            Aapt2Exists: true,
            ZipAlignExists: true,
            D8Exists: true,
            ApkSignerExists: true,
            SourcePropertiesPath: "C:\\Android\\Sdk\\build-tools\\36.0.0\\source.properties",
            RawSourceProperties: "Pkg.Revision=36.0.0\n",
            Message: null);
        var buildToolsResult = new AndroidBuildToolsDetectionResult(
            AndroidBuildToolsDetectionStatus.Succeeded,
            "C:\\Android\\Sdk",
            new[] { buildToolsPackage },
            "Detected 1 usable Android build-tools package(s).");

        using var viewModel = new EnvironmentDoctorViewModel(
            new StubEnvironmentScanner(new ToolStatus(
                "Git",
                Installed: true,
                Version: "2.55.0.windows.3",
                Path: "C:\\Program Files\\Git\\cmd\\git.exe",
                Message: "Ready")),
            new StubFlutterDetector(new FlutterDetectionResult(
                FlutterSdkDetectionStatus.Succeeded,
                Installed: true,
                FlutterPath: "C:\\flutter\\bin\\flutter.bat",
                FlutterSdkPath: "C:\\flutter",
                FlutterVersion: "3.44.8",
                Channel: "stable",
                Candidates: Array.Empty<FlutterSdkCandidate>(),
                HasConflict: false,
                Message: "Ready")),
            new StubJavaDetector(new JavaDetectionResult(
                JavaDetectionStatus.Succeeded,
                new JavaInstallation(
                    "C:\\Java\\jdk-17\\bin\\java.exe",
                    "C:\\Java\\jdk-17",
                    "17.0.12",
                    "Temurin",
                    "amd64",
                    IsJdk: true,
                    "C:\\Java\\jdk-17\\bin\\javac.exe",
                    PathIndex: 0,
                    ResolutionOrder: 0,
                    IsPreferred: true,
                    IsShadowed: false),
                Array.Empty<JavaInstallation>(),
                HasConflict: false,
                EmptyDiscovery("java.exe"),
                Message: "Ready")),
            new StubEnvironmentVariableReader(snapshot),
            new StubAndroidSdkRootDetector(sdkResult),
            new StubAndroidCommandLineToolsDetector(new AndroidCommandLineToolsDetectionResult(
                AndroidCommandLineToolsDetectionStatus.Succeeded,
                "C:\\Android\\Sdk",
                commandLineCandidate,
                new[] { commandLineCandidate },
                HasMultipleInstallations: false,
                Message: "Android command-line tools 19.0 detected.")),
            new StubAndroidAdbDetector(adbResult),
            new StubAndroidPlatformDetector(platformResult),
            new StubAndroidBuildToolsDetector(buildToolsResult));

        await viewModel.ScanCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasScanned);
        Assert.False(viewModel.IsBusy);
        Assert.Equal("Environment scan complete.", viewModel.StatusMessage);
        Assert.Contains("2.55.0", viewModel.GitSummary, StringComparison.Ordinal);
        Assert.Contains("3.44.8", viewModel.FlutterSummary, StringComparison.Ordinal);
        Assert.Contains("17.0.12", viewModel.JavaSummary, StringComparison.Ordinal);
        Assert.Equal("Ready", viewModel.AndroidSdkSummary);
        Assert.Contains("C:\\Android\\Sdk", viewModel.AndroidSdkDetails, StringComparison.Ordinal);
        Assert.Contains("19.0", viewModel.CommandLineToolsSummary, StringComparison.Ordinal);
        Assert.Contains("sdkmanager.bat", viewModel.CommandLineToolsDetails, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ADB 1.0.41", viewModel.AdbSummary, StringComparison.Ordinal);
        Assert.Contains("platform-tools 36.0.0-13206524", viewModel.AdbSummary, StringComparison.Ordinal);
        Assert.Contains("adb.exe", viewModel.AdbDetails, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("API 35", viewModel.AndroidPlatformsSummary);
        Assert.Contains("android-35: ready rev 2", viewModel.AndroidPlatformsDetails, StringComparison.Ordinal);
        Assert.Equal("36.0.0 • 1 usable", viewModel.AndroidBuildToolsSummary);
        Assert.Contains("36.0.0: ready", viewModel.AndroidBuildToolsDetails, StringComparison.Ordinal);
        Assert.NotNull(viewModel.LastScannedAt);
    }

    private static EnvironmentVariableSnapshot CreateEnvironmentSnapshot()
        => new(
            DateTimeOffset.UtcNow,
            Variable("PATH", "C:\\flutter\\bin"),
            Variable("JAVA_HOME", "C:\\Java\\jdk-17"),
            Variable("ANDROID_HOME", "C:\\Android\\Sdk"),
            Variable("ANDROID_SDK_ROOT", "C:\\Android\\Sdk"));

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
            Array.Empty<IgnoredPathEntry>());

    private sealed class StubEnvironmentScanner : IEnvironmentScanner
    {
        private readonly ToolStatus _status;

        public StubEnvironmentScanner(ToolStatus status) => _status = status;

        public Task<IReadOnlyList<ToolStatus>> ScanAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ToolStatus>>(new[] { _status });
    }

    private sealed class StubFlutterDetector : IFlutterSdkDetector
    {
        private readonly FlutterDetectionResult _result;

        public StubFlutterDetector(FlutterDetectionResult result) => _result = result;

        public Task<FlutterDetectionResult> DetectAsync(
            FlutterSdkDetectionRequest? request = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
    }

    private sealed class StubJavaDetector : IJavaInstallationDetector
    {
        private readonly JavaDetectionResult _result;

        public StubJavaDetector(JavaDetectionResult result) => _result = result;

        public Task<JavaDetectionResult> DetectAsync(
            JavaDetectionRequest? request = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
    }

    private sealed class StubEnvironmentVariableReader : IEnvironmentVariableReader
    {
        private readonly EnvironmentVariableSnapshot _snapshot;

        public StubEnvironmentVariableReader(EnvironmentVariableSnapshot snapshot) => _snapshot = snapshot;

        public EnvironmentVariableSnapshot Read() => _snapshot;
    }

    private sealed class StubAndroidSdkRootDetector : IAndroidSdkRootDetector
    {
        private readonly AndroidSdkRootDetectionResult _result;

        public StubAndroidSdkRootDetector(AndroidSdkRootDetectionResult result) => _result = result;

        public AndroidSdkRootDetectionResult Detect(EnvironmentVariableSnapshot snapshot) => _result;
    }

    private sealed class StubAndroidCommandLineToolsDetector : IAndroidCommandLineToolsDetector
    {
        private readonly AndroidCommandLineToolsDetectionResult _result;

        public StubAndroidCommandLineToolsDetector(AndroidCommandLineToolsDetectionResult result) => _result = result;

        public AndroidCommandLineToolsDetectionResult Detect(AndroidSdkRootDetectionResult sdkRootResult) => _result;
    }

    private sealed class StubAndroidAdbDetector : IAndroidAdbDetector
    {
        private readonly AndroidAdbDetectionResult _result;

        public StubAndroidAdbDetector(AndroidAdbDetectionResult result) => _result = result;

        public Task<AndroidAdbDetectionResult> DetectAsync(
            AndroidSdkRootDetectionResult sdkRootResult,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
    }

    private sealed class StubAndroidPlatformDetector : IAndroidPlatformDetector
    {
        private readonly AndroidPlatformDetectionResult _result;

        public StubAndroidPlatformDetector(AndroidPlatformDetectionResult result) => _result = result;

        public AndroidPlatformDetectionResult Detect(AndroidSdkRootDetectionResult sdkRootResult) => _result;
    }

    private sealed class StubAndroidBuildToolsDetector : IAndroidBuildToolsDetector
    {
        private readonly AndroidBuildToolsDetectionResult _result;

        public StubAndroidBuildToolsDetector(AndroidBuildToolsDetectionResult result) => _result = result;

        public AndroidBuildToolsDetectionResult Detect(AndroidSdkRootDetectionResult sdkRootResult) => _result;
    }
}
