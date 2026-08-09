using FlutterBuildDoctor.Android.Detection;
using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Flutter.Detection;

namespace FlutterBuildDoctor.App.EnvironmentSnapshots;

public sealed record EnvironmentSnapshot(
    DateTimeOffset CapturedAt,
    DateTimeOffset CompletedAt,
    WindowsEnvironmentInfo Windows,
    EnvironmentVariableSnapshot EnvironmentVariables,
    FlutterDetectionResult Flutter,
    DartDetectionResult Dart,
    JavaDetectionResult Java,
    AndroidSdkRootDetectionResult AndroidSdk,
    AndroidCommandLineToolsDetectionResult AndroidCommandLineTools,
    AndroidAdbDetectionResult Adb,
    AndroidPlatformDetectionResult AndroidPlatforms,
    AndroidBuildToolsDetectionResult AndroidBuildTools,
    AndroidEmulatorDetectionResult Emulator,
    AndroidAvdManagerDetectionResult AvdManager,
    AndroidLicenseDetectionResult AndroidLicenses,
    AndroidStudioDetectionResult AndroidStudio)
{
    public TimeSpan CaptureDuration => CompletedAt - CapturedAt;
}

public interface IEnvironmentSnapshotService
{
    Task<EnvironmentSnapshot> CaptureAsync(CancellationToken cancellationToken = default);
}
