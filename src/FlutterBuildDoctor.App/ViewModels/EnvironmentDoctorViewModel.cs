using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlutterBuildDoctor.Android.Detection;
using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Application.Services;
using FlutterBuildDoctor.Domain.Environment;
using FlutterBuildDoctor.Flutter.Detection;

namespace FlutterBuildDoctor.App.ViewModels;

public sealed partial class EnvironmentDoctorViewModel : ObservableObject, IDisposable
{
    private readonly IEnvironmentScanner _environmentScanner;
    private readonly IFlutterSdkDetector _flutterSdkDetector;
    private readonly IJavaInstallationDetector _javaInstallationDetector;
    private readonly IEnvironmentVariableReader _environmentVariableReader;
    private readonly IAndroidSdkRootDetector _androidSdkRootDetector;
    private readonly IAndroidCommandLineToolsDetector _androidCommandLineToolsDetector;
    private readonly IAndroidAdbDetector _androidAdbDetector;
    private readonly IAndroidPlatformDetector _androidPlatformDetector;
    private CancellationTokenSource? _scanCancellation;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasScanned;

    [ObservableProperty]
    private string _statusMessage = "Environment has not been scanned yet.";

    [ObservableProperty]
    private string _gitSummary = "Not scanned";

    [ObservableProperty]
    private string _gitDetails = "Run Environment Doctor to detect Git.";

    [ObservableProperty]
    private string _flutterSummary = "Not scanned";

    [ObservableProperty]
    private string _flutterDetails = "Run Environment Doctor to detect the effective Flutter SDK.";

    [ObservableProperty]
    private string _javaSummary = "Not scanned";

    [ObservableProperty]
    private string _javaDetails = "Run Environment Doctor to detect Java/JDK installations.";

    [ObservableProperty]
    private string _androidSdkSummary = "Not scanned";

    [ObservableProperty]
    private string _androidSdkDetails = "Run Environment Doctor to validate ANDROID_HOME / ANDROID_SDK_ROOT.";

    [ObservableProperty]
    private string _commandLineToolsSummary = "Not scanned";

    [ObservableProperty]
    private string _commandLineToolsDetails = "Run Environment Doctor to detect sdkmanager and Android command-line tools.";

    [ObservableProperty]
    private string _adbSummary = "Not scanned";

    [ObservableProperty]
    private string _adbDetails = "Run Environment Doctor to detect Android platform-tools / ADB.";

    [ObservableProperty]
    private string _androidPlatformsSummary = "Not scanned";

    [ObservableProperty]
    private string _androidPlatformsDetails = "Run Environment Doctor to inventory installed Android platforms.";

    [ObservableProperty]
    private DateTimeOffset? _lastScannedAt;

    public EnvironmentDoctorViewModel(
        IEnvironmentScanner environmentScanner,
        IFlutterSdkDetector flutterSdkDetector,
        IJavaInstallationDetector javaInstallationDetector,
        IEnvironmentVariableReader environmentVariableReader,
        IAndroidSdkRootDetector androidSdkRootDetector,
        IAndroidCommandLineToolsDetector androidCommandLineToolsDetector,
        IAndroidAdbDetector androidAdbDetector,
        IAndroidPlatformDetector androidPlatformDetector)
    {
        _environmentScanner = environmentScanner ?? throw new ArgumentNullException(nameof(environmentScanner));
        _flutterSdkDetector = flutterSdkDetector ?? throw new ArgumentNullException(nameof(flutterSdkDetector));
        _javaInstallationDetector = javaInstallationDetector ?? throw new ArgumentNullException(nameof(javaInstallationDetector));
        _environmentVariableReader = environmentVariableReader ?? throw new ArgumentNullException(nameof(environmentVariableReader));
        _androidSdkRootDetector = androidSdkRootDetector ?? throw new ArgumentNullException(nameof(androidSdkRootDetector));
        _androidCommandLineToolsDetector = androidCommandLineToolsDetector ?? throw new ArgumentNullException(nameof(androidCommandLineToolsDetector));
        _androidAdbDetector = androidAdbDetector ?? throw new ArgumentNullException(nameof(androidAdbDetector));
        _androidPlatformDetector = androidPlatformDetector ?? throw new ArgumentNullException(nameof(androidPlatformDetector));
    }

    public bool CanScan => !IsBusy;

    public bool CanCancel => IsBusy;

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanScan));
        OnPropertyChanged(nameof(CanCancel));
        ScanCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRunScan))]
    private async Task ScanAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Scanning Git, Flutter, Java, Android SDK, sdkmanager, ADB and installed platforms...";
        _scanCancellation = new CancellationTokenSource();

        try
        {
            var token = _scanCancellation.Token;

            var toolStatuses = await _environmentScanner.ScanAsync(token);
            ApplyGit(toolStatuses.FirstOrDefault(tool =>
                string.Equals(tool.Name, "Git", StringComparison.OrdinalIgnoreCase)));

            var flutter = await _flutterSdkDetector.DetectAsync(cancellationToken: token);
            ApplyFlutter(flutter);

            var java = await _javaInstallationDetector.DetectAsync(cancellationToken: token);
            ApplyJava(java);

            token.ThrowIfCancellationRequested();
            var environment = _environmentVariableReader.Read();
            var androidSdk = _androidSdkRootDetector.Detect(environment);
            ApplyAndroidSdk(androidSdk, environment);

            token.ThrowIfCancellationRequested();
            var commandLineTools = _androidCommandLineToolsDetector.Detect(androidSdk);
            ApplyCommandLineTools(commandLineTools);

            token.ThrowIfCancellationRequested();
            var platforms = _androidPlatformDetector.Detect(androidSdk);
            ApplyAndroidPlatforms(platforms);

            token.ThrowIfCancellationRequested();
            var adb = await _androidAdbDetector.DetectAsync(androidSdk, token);
            ApplyAdb(adb);

            HasScanned = true;
            LastScannedAt = DateTimeOffset.Now;
            StatusMessage = "Environment scan complete.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Environment scan cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Environment scan failed unexpectedly: {ex.Message}";
        }
        finally
        {
            _scanCancellation?.Dispose();
            _scanCancellation = null;
            IsBusy = false;
        }
    }

    private bool CanRunScan() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanCancelScan))]
    private void Cancel()
    {
        if (_scanCancellation is null || _scanCancellation.IsCancellationRequested)
        {
            return;
        }

        StatusMessage = "Cancelling environment scan...";
        _scanCancellation.Cancel();
    }

    private bool CanCancelScan() => IsBusy;

    private void ApplyGit(ToolStatus? git)
    {
        if (git is null)
        {
            GitSummary = "Unavailable";
            GitDetails = "No Git detector result was returned.";
            return;
        }

        GitSummary = git.Installed
            ? git.Version ?? "Installed"
            : "Missing";
        GitDetails = JoinDetails(git.Path, git.Message);
    }

    private void ApplyFlutter(FlutterDetectionResult flutter)
    {
        FlutterSummary = flutter.IsSuccess
            ? JoinSummary(flutter.FlutterVersion, flutter.Channel)
            : flutter.Status.ToString();
        FlutterDetails = JoinDetails(flutter.FlutterSdkPath ?? flutter.FlutterPath, flutter.Message);
    }

    private void ApplyJava(JavaDetectionResult java)
    {
        var preferred = java.PreferredInstallation;
        if (preferred is null)
        {
            JavaSummary = java.Status == JavaDetectionStatus.Missing
                ? "Missing"
                : java.Status.ToString();
            JavaDetails = java.Message ?? "No effective Java installation was selected.";
            return;
        }

        var kind = preferred.IsJdk ? "JDK" : "JRE";
        JavaSummary = JoinSummary(preferred.Version, kind);
        JavaDetails = JoinDetails(
            preferred.JavaHome ?? preferred.ExecutablePath,
            JoinSummary(preferred.Vendor, java.Message));
    }

    private void ApplyAndroidSdk(
        AndroidSdkRootDetectionResult result,
        EnvironmentVariableSnapshot environment)
    {
        var candidate = result.EffectiveCandidate;
        AndroidSdkSummary = result.IsSuccess
            ? "Ready"
            : result.Status switch
            {
                AndroidSdkRootDetectionStatus.MissingEffectiveRoot => "Missing",
                AndroidSdkRootDetectionStatus.EffectiveRootInvalid => "Invalid",
                _ => result.Status.ToString()
            };

        var effectiveVariable = environment.AndroidSdkRoot.EffectiveValue ?? environment.AndroidHome.EffectiveValue;
        AndroidSdkDetails = JoinDetails(
            candidate?.NormalizedPath ?? effectiveVariable,
            result.Message);
    }

    private void ApplyCommandLineTools(AndroidCommandLineToolsDetectionResult result)
    {
        var candidate = result.EffectiveCandidate;
        CommandLineToolsSummary = result.IsSuccess
            ? JoinSummary(candidate?.Revision, "sdkmanager")
            : result.Status switch
            {
                AndroidCommandLineToolsDetectionStatus.CommandLineToolsMissing => "Missing",
                AndroidCommandLineToolsDetectionStatus.EffectiveSdkManagerMissing => "sdkmanager missing",
                AndroidCommandLineToolsDetectionStatus.MetadataInvalid => "Metadata invalid",
                AndroidCommandLineToolsDetectionStatus.AndroidSdkRootUnavailable => "SDK unavailable",
                _ => result.Status.ToString()
            };
        CommandLineToolsDetails = JoinDetails(candidate?.SdkManagerPath ?? candidate?.InstallationPath, result.Message);
    }

    private void ApplyAndroidPlatforms(AndroidPlatformDetectionResult result)
    {
        AndroidPlatformsSummary = result.IsSuccess
            ? result.InstalledApiLevels.Count == 0
                ? "No usable platforms"
                : string.Join(" • ", result.InstalledApiLevels.Select(api => $"API {api}"))
            : result.Status switch
            {
                AndroidPlatformDetectionStatus.PlatformsDirectoryMissing => "platforms missing",
                AndroidPlatformDetectionStatus.NoPlatformsInstalled => "None installed",
                AndroidPlatformDetectionStatus.PartialInstallationsOnly => "Partial/broken only",
                AndroidPlatformDetectionStatus.AndroidSdkRootUnavailable => "SDK unavailable",
                AndroidPlatformDetectionStatus.InspectionFailed => "Inspection failed",
                _ => result.Status.ToString()
            };

        var packageEvidence = result.Platforms.Count == 0
            ? null
            : string.Join(
                " | ",
                result.Platforms.Select(platform =>
                {
                    var readiness = platform.IsUsable ? "ready" : "partial";
                    var revision = string.IsNullOrWhiteSpace(platform.Revision) ? string.Empty : $" rev {platform.Revision}";
                    var preview = platform.IsPreview && !string.IsNullOrWhiteSpace(platform.CodeName)
                        ? $" {platform.CodeName}"
                        : string.Empty;
                    return $"{platform.PackageId}: {readiness}{revision}{preview}";
                }));

        AndroidPlatformsDetails = JoinDetails(packageEvidence, result.Message);
    }

    private void ApplyAdb(AndroidAdbDetectionResult result)
    {
        AdbSummary = result.IsSuccess
            ? JoinSummary(
                result.AdbProtocolVersion is null ? null : $"ADB {result.AdbProtocolVersion}",
                result.PlatformToolsVersion is null ? null : $"platform-tools {result.PlatformToolsVersion}")
            : result.Status switch
            {
                AndroidAdbDetectionStatus.PlatformToolsMissing => "platform-tools missing",
                AndroidAdbDetectionStatus.AdbMissing => "ADB missing",
                AndroidAdbDetectionStatus.AndroidSdkRootUnavailable => "SDK unavailable",
                AndroidAdbDetectionStatus.TimedOut => "Probe timed out",
                AndroidAdbDetectionStatus.ProbeFailed => "Probe failed",
                AndroidAdbDetectionStatus.ParseFailed => "Version parse failed",
                AndroidAdbDetectionStatus.Cancelled => "Cancelled",
                _ => result.Status.ToString()
            };
        AdbDetails = JoinDetails(result.AdbPath ?? result.PlatformToolsPath, result.Message);
    }

    private static string JoinSummary(string? primary, string? secondary)
    {
        if (string.IsNullOrWhiteSpace(primary))
        {
            return string.IsNullOrWhiteSpace(secondary) ? "Unknown" : secondary;
        }

        return string.IsNullOrWhiteSpace(secondary)
            ? primary
            : $"{primary} • {secondary}";
    }

    private static string JoinDetails(string? path, string? message)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.IsNullOrWhiteSpace(message) ? "No details available." : message;
        }

        return string.IsNullOrWhiteSpace(message)
            ? path
            : $"{path} • {message}";
    }

    public void Dispose()
    {
        _scanCancellation?.Cancel();
        _scanCancellation?.Dispose();
        _scanCancellation = null;
    }
}
