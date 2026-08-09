using FlutterBuildDoctor.Android.Detection;
using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Flutter.Detection;

namespace FlutterBuildDoctor.App.Environment;

public sealed class EnvironmentSnapshotService : IEnvironmentSnapshotService
{
    private readonly IWindowsEnvironmentDetector _windowsDetector;
    private readonly IEnvironmentVariableReader _environmentVariableReader;
    private readonly IFlutterSdkDetector _flutterDetector;
    private readonly IDartSdkDetector _dartDetector;
    private readonly IJavaInstallationDetector _javaDetector;
    private readonly IAndroidSdkRootDetector _androidSdkRootDetector;
    private readonly IAndroidCommandLineToolsDetector _commandLineToolsDetector;
    private readonly IAndroidAdbDetector _adbDetector;
    private readonly IAndroidPlatformDetector _platformDetector;
    private readonly IAndroidBuildToolsDetector _buildToolsDetector;
    private readonly IAndroidEmulatorDetector _emulatorDetector;
    private readonly IAndroidAvdManagerDetector _avdManagerDetector;
    private readonly IAndroidLicenseDetector _licenseDetector;
    private readonly IAndroidStudioDetector _androidStudioDetector;

    public EnvironmentSnapshotService(
        IWindowsEnvironmentDetector windowsDetector,
        IEnvironmentVariableReader environmentVariableReader,
        IFlutterSdkDetector flutterDetector,
        IDartSdkDetector dartDetector,
        IJavaInstallationDetector javaDetector,
        IAndroidSdkRootDetector androidSdkRootDetector,
        IAndroidCommandLineToolsDetector commandLineToolsDetector,
        IAndroidAdbDetector adbDetector,
        IAndroidPlatformDetector platformDetector,
        IAndroidBuildToolsDetector buildToolsDetector,
        IAndroidEmulatorDetector emulatorDetector,
        IAndroidAvdManagerDetector avdManagerDetector,
        IAndroidLicenseDetector licenseDetector,
        IAndroidStudioDetector androidStudioDetector)
    {
        _windowsDetector = windowsDetector ?? throw new ArgumentNullException(nameof(windowsDetector));
        _environmentVariableReader = environmentVariableReader ?? throw new ArgumentNullException(nameof(environmentVariableReader));
        _flutterDetector = flutterDetector ?? throw new ArgumentNullException(nameof(flutterDetector));
        _dartDetector = dartDetector ?? throw new ArgumentNullException(nameof(dartDetector));
        _javaDetector = javaDetector ?? throw new ArgumentNullException(nameof(javaDetector));
        _androidSdkRootDetector = androidSdkRootDetector ?? throw new ArgumentNullException(nameof(androidSdkRootDetector));
        _commandLineToolsDetector = commandLineToolsDetector ?? throw new ArgumentNullException(nameof(commandLineToolsDetector));
        _adbDetector = adbDetector ?? throw new ArgumentNullException(nameof(adbDetector));
        _platformDetector = platformDetector ?? throw new ArgumentNullException(nameof(platformDetector));
        _buildToolsDetector = buildToolsDetector ?? throw new ArgumentNullException(nameof(buildToolsDetector));
        _emulatorDetector = emulatorDetector ?? throw new ArgumentNullException(nameof(emulatorDetector));
        _avdManagerDetector = avdManagerDetector ?? throw new ArgumentNullException(nameof(avdManagerDetector));
        _licenseDetector = licenseDetector ?? throw new ArgumentNullException(nameof(licenseDetector));
        _androidStudioDetector = androidStudioDetector ?? throw new ArgumentNullException(nameof(androidStudioDetector));
    }

    public async Task<EnvironmentSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var capturedAt = DateTimeOffset.UtcNow;

        var windows = _windowsDetector.Detect();
        var environmentVariables = _environmentVariableReader.Read();
        cancellationToken.ThrowIfCancellationRequested();

        var pathValue = environmentVariables.Path.EffectiveValue;
        var flutterTask = _flutterDetector.DetectAsync(
            new FlutterSdkDetectionRequest(PathValue: pathValue),
            cancellationToken);
        var javaTask = _javaDetector.DetectAsync(
            new JavaDetectionRequest(PathValue: pathValue),
            cancellationToken);

        var androidStudio = _androidStudioDetector.Detect(windows);
        var androidSdk = _androidSdkRootDetector.Detect(environmentVariables);
        var commandLineTools = _commandLineToolsDetector.Detect(androidSdk);
        var androidPlatforms = _platformDetector.Detect(androidSdk);
        var androidBuildTools = _buildToolsDetector.Detect(androidSdk);
        var avdManager = _avdManagerDetector.Detect(commandLineTools);

        var adbTask = _adbDetector.DetectAsync(androidSdk, cancellationToken);
        var emulatorTask = _emulatorDetector.DetectAsync(androidSdk, cancellationToken);
        var licenseTask = _licenseDetector.DetectAsync(commandLineTools, cancellationToken);

        var flutter = await flutterTask.ConfigureAwait(false);
        var dartTask = _dartDetector.DetectAsync(
            flutter,
            new DartSdkDetectionRequest(PathValue: pathValue),
            cancellationToken);

        await Task.WhenAll(javaTask, adbTask, emulatorTask, licenseTask, dartTask).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return new EnvironmentSnapshot(
            capturedAt,
            DateTimeOffset.UtcNow,
            windows,
            environmentVariables,
            flutter,
            await dartTask.ConfigureAwait(false),
            await javaTask.ConfigureAwait(false),
            androidSdk,
            commandLineTools,
            await adbTask.ConfigureAwait(false),
            androidPlatforms,
            androidBuildTools,
            await emulatorTask.ConfigureAwait(false),
            avdManager,
            await licenseTask.ConfigureAwait(false),
            androidStudio);
    }
}
