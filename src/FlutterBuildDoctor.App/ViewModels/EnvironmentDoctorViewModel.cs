using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlutterBuildDoctor.Android.Detection;
using FlutterBuildDoctor.App.EnvironmentSnapshots;
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
    private readonly IAndroidBuildToolsDetector _androidBuildToolsDetector;
    private readonly IAndroidEmulatorDetector _androidEmulatorDetector;
    private readonly IAndroidAvdManagerDetector _androidAvdManagerDetector;
    private readonly IAndroidLicenseDetector? _androidLicenseDetector;
    private readonly IWindowsEnvironmentDetector? _windowsEnvironmentDetector;
    private readonly IAndroidStudioDetector? _androidStudioDetector;
    private readonly IDartSdkDetector? _dartSdkDetector;
    private readonly IEnvironmentSnapshotService? _environmentSnapshotService;
    private CancellationTokenSource? _scanCancellation;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasScanned;
    [ObservableProperty] private string _statusMessage = "Environment has not been scanned yet.";
    [ObservableProperty] private string _windowsSummary = "Not scanned";
    [ObservableProperty] private string _windowsDetails = "Run Environment Doctor to detect Windows version, build and architecture.";
    [ObservableProperty] private string _androidStudioSummary = "Not scanned";
    [ObservableProperty] private string _androidStudioDetails = "Run Environment Doctor to detect Android Studio installations.";
    [ObservableProperty] private string _gitSummary = "Not scanned";
    [ObservableProperty] private string _gitDetails = "Run Environment Doctor to detect Git.";
    [ObservableProperty] private string _flutterSummary = "Not scanned";
    [ObservableProperty] private string _flutterDetails = "Run Environment Doctor to detect the effective Flutter SDK.";
    [ObservableProperty] private string _dartSummary = "Not scanned";
    [ObservableProperty] private string _dartDetails = "Run Environment Doctor to detect Flutter-bundled and PATH Dart SDKs.";
    [ObservableProperty] private string _javaSummary = "Not scanned";
    [ObservableProperty] private string _javaDetails = "Run Environment Doctor to detect Java/JDK installations.";
    [ObservableProperty] private string _androidSdkSummary = "Not scanned";
    [ObservableProperty] private string _androidSdkDetails = "Run Environment Doctor to validate ANDROID_HOME / ANDROID_SDK_ROOT.";
    [ObservableProperty] private string _commandLineToolsSummary = "Not scanned";
    [ObservableProperty] private string _commandLineToolsDetails = "Run Environment Doctor to detect sdkmanager and Android command-line tools.";
    [ObservableProperty] private string _avdManagerSummary = "Not scanned";
    [ObservableProperty] private string _avdManagerDetails = "Run Environment Doctor to detect avdmanager availability.";
    [ObservableProperty] private string _androidLicenseSummary = "Not scanned";
    [ObservableProperty] private string _androidLicenseDetails = "Run Environment Doctor to check Android SDK license readiness safely.";
    [ObservableProperty] private string _adbSummary = "Not scanned";
    [ObservableProperty] private string _adbDetails = "Run Environment Doctor to detect Android platform-tools / ADB.";
    [ObservableProperty] private string _androidPlatformsSummary = "Not scanned";
    [ObservableProperty] private string _androidPlatformsDetails = "Run Environment Doctor to inventory installed Android platforms.";
    [ObservableProperty] private string _androidBuildToolsSummary = "Not scanned";
    [ObservableProperty] private string _androidBuildToolsDetails = "Run Environment Doctor to inventory installed Android build-tools.";
    [ObservableProperty] private string _androidEmulatorSummary = "Not scanned";
    [ObservableProperty] private string _androidEmulatorDetails = "Run Environment Doctor to detect the Android emulator binary and version.";
    [ObservableProperty] private DateTimeOffset? _lastScannedAt;

    public EnvironmentDoctorViewModel(
        IEnvironmentScanner environmentScanner,
        IFlutterSdkDetector flutterSdkDetector,
        IJavaInstallationDetector javaInstallationDetector,
        IEnvironmentVariableReader environmentVariableReader,
        IAndroidSdkRootDetector androidSdkRootDetector,
        IAndroidCommandLineToolsDetector androidCommandLineToolsDetector,
        IAndroidAdbDetector androidAdbDetector,
        IAndroidPlatformDetector androidPlatformDetector,
        IAndroidBuildToolsDetector androidBuildToolsDetector,
        IAndroidEmulatorDetector androidEmulatorDetector,
        IAndroidAvdManagerDetector androidAvdManagerDetector,
        IAndroidLicenseDetector? androidLicenseDetector = null,
        IWindowsEnvironmentDetector? windowsEnvironmentDetector = null,
        IAndroidStudioDetector? androidStudioDetector = null,
        IDartSdkDetector? dartSdkDetector = null,
        IEnvironmentSnapshotService? environmentSnapshotService = null)
    {
        _environmentScanner = environmentScanner ?? throw new ArgumentNullException(nameof(environmentScanner));
        _flutterSdkDetector = flutterSdkDetector ?? throw new ArgumentNullException(nameof(flutterSdkDetector));
        _javaInstallationDetector = javaInstallationDetector ?? throw new ArgumentNullException(nameof(javaInstallationDetector));
        _environmentVariableReader = environmentVariableReader ?? throw new ArgumentNullException(nameof(environmentVariableReader));
        _androidSdkRootDetector = androidSdkRootDetector ?? throw new ArgumentNullException(nameof(androidSdkRootDetector));
        _androidCommandLineToolsDetector = androidCommandLineToolsDetector ?? throw new ArgumentNullException(nameof(androidCommandLineToolsDetector));
        _androidAdbDetector = androidAdbDetector ?? throw new ArgumentNullException(nameof(androidAdbDetector));
        _androidPlatformDetector = androidPlatformDetector ?? throw new ArgumentNullException(nameof(androidPlatformDetector));
        _androidBuildToolsDetector = androidBuildToolsDetector ?? throw new ArgumentNullException(nameof(androidBuildToolsDetector));
        _androidEmulatorDetector = androidEmulatorDetector ?? throw new ArgumentNullException(nameof(androidEmulatorDetector));
        _androidAvdManagerDetector = androidAvdManagerDetector ?? throw new ArgumentNullException(nameof(androidAvdManagerDetector));
        _androidLicenseDetector = androidLicenseDetector;
        _windowsEnvironmentDetector = windowsEnvironmentDetector;
        _androidStudioDetector = androidStudioDetector;
        _dartSdkDetector = dartSdkDetector;
        _environmentSnapshotService = environmentSnapshotService;
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
        if (IsBusy) return;

        IsBusy = true;
        StatusMessage = _environmentSnapshotService is null
            ? "Scanning Windows, Android Studio, Git, Flutter, Dart, Java, Android SDK, sdkmanager, avdmanager, licenses, ADB, installed platforms, build-tools and emulator..."
            : "Capturing one consistent environment snapshot and Git readiness...";
        _scanCancellation = new CancellationTokenSource();

        try
        {
            var token = _scanCancellation.Token;
            var toolStatuses = await _environmentScanner.ScanAsync(token);
            ApplyGit(toolStatuses.FirstOrDefault(tool => string.Equals(tool.Name, "Git", StringComparison.OrdinalIgnoreCase)));

            if (_environmentSnapshotService is not null)
            {
                var snapshot = await _environmentSnapshotService.CaptureAsync(token);
                ApplySnapshot(snapshot);
            }
            else
            {
                await ScanLegacyDetectorsAsync(token);
            }

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

    private async Task ScanLegacyDetectorsAsync(CancellationToken token)
    {
        WindowsEnvironmentInfo? windows = null;
        if (_windowsEnvironmentDetector is not null)
        {
            token.ThrowIfCancellationRequested();
            windows = _windowsEnvironmentDetector.Detect();
            ApplyWindows(windows);
            if (_androidStudioDetector is not null)
            {
                token.ThrowIfCancellationRequested();
                ApplyAndroidStudio(_androidStudioDetector.Detect(windows));
            }
        }

        var flutter = await _flutterSdkDetector.DetectAsync(cancellationToken: token);
        ApplyFlutter(flutter);
        if (_dartSdkDetector is not null)
        {
            token.ThrowIfCancellationRequested();
            ApplyDart(await _dartSdkDetector.DetectAsync(flutter, cancellationToken: token));
        }

        ApplyJava(await _javaInstallationDetector.DetectAsync(cancellationToken: token));

        token.ThrowIfCancellationRequested();
        var environment = _environmentVariableReader.Read();
        var androidSdk = _androidSdkRootDetector.Detect(environment);
        ApplyAndroidSdk(androidSdk, environment);

        token.ThrowIfCancellationRequested();
        var commandLineTools = _androidCommandLineToolsDetector.Detect(androidSdk);
        ApplyCommandLineTools(commandLineTools);

        token.ThrowIfCancellationRequested();
        ApplyAvdManager(_androidAvdManagerDetector.Detect(commandLineTools));

        if (_androidLicenseDetector is not null)
        {
            token.ThrowIfCancellationRequested();
            ApplyAndroidLicenses(await _androidLicenseDetector.DetectAsync(commandLineTools, token));
        }

        token.ThrowIfCancellationRequested();
        ApplyAndroidPlatforms(_androidPlatformDetector.Detect(androidSdk));
        token.ThrowIfCancellationRequested();
        ApplyAndroidBuildTools(_androidBuildToolsDetector.Detect(androidSdk));
        token.ThrowIfCancellationRequested();
        ApplyAndroidEmulator(await _androidEmulatorDetector.DetectAsync(androidSdk, token));
        token.ThrowIfCancellationRequested();
        ApplyAdb(await _androidAdbDetector.DetectAsync(androidSdk, token));
    }

    private void ApplySnapshot(EnvironmentSnapshot snapshot)
    {
        ApplyWindows(snapshot.Windows);
        ApplyAndroidStudio(snapshot.AndroidStudio);
        ApplyFlutter(snapshot.Flutter);
        ApplyDart(snapshot.Dart);
        ApplyJava(snapshot.Java);
        ApplyAndroidSdk(snapshot.AndroidSdk, snapshot.EnvironmentVariables);
        ApplyCommandLineTools(snapshot.AndroidCommandLineTools);
        ApplyAvdManager(snapshot.AvdManager);
        ApplyAndroidLicenses(snapshot.AndroidLicenses);
        ApplyAndroidPlatforms(snapshot.AndroidPlatforms);
        ApplyAndroidBuildTools(snapshot.AndroidBuildTools);
        ApplyAndroidEmulator(snapshot.Emulator);
        ApplyAdb(snapshot.Adb);
    }

    private bool CanRunScan() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanCancelScan))]
    private void Cancel()
    {
        if (_scanCancellation is null || _scanCancellation.IsCancellationRequested) return;
        StatusMessage = "Cancelling environment scan...";
        _scanCancellation.Cancel();
    }

    private bool CanCancelScan() => IsBusy;

    private void ApplyWindows(WindowsEnvironmentInfo result)
    {
        WindowsSummary = result.IsSuccess
            ? JoinSummary(result.Description, result.BuildNumber is null ? result.OsArchitecture : $"build {result.BuildNumber} • {result.OsArchitecture}")
            : result.Status switch
            {
                WindowsEnvironmentDetectionStatus.NotWindows => "Not Windows",
                WindowsEnvironmentDetectionStatus.Unavailable => "Unavailable",
                _ => result.Status.ToString()
            };
        var bitness = result.Is64BitOperatingSystem ? "64-bit OS" : "32-bit OS";
        var process = string.IsNullOrWhiteSpace(result.ProcessArchitecture) ? null : $"process {result.ProcessArchitecture}";
        WindowsDetails = JoinDetails(result.Version, JoinSummary(bitness, JoinSummary(process, result.Message)));
    }

    private void ApplyAndroidStudio(AndroidStudioDetectionResult result)
    {
        if (result.IsSuccess)
        {
            var primary = result.Installations.FirstOrDefault();
            AndroidStudioSummary = primary is null
                ? "Detected"
                : JoinSummary(primary.Version ?? primary.BuildNumber, result.Installations.Count == 1 ? "1 installation" : $"{result.Installations.Count} installations");
        }
        else
        {
            AndroidStudioSummary = result.Status switch
            {
                AndroidStudioDetectionStatus.NotWindows => "Not Windows",
                AndroidStudioDetectionStatus.Missing => "Not found",
                AndroidStudioDetectionStatus.InspectionFailed => "Inspection failed",
                _ => result.Status.ToString()
            };
        }

        var evidence = result.Installations.Count == 0
            ? null
            : string.Join(" | ", result.Installations.Select(installation =>
            {
                var identity = JoinSummary(installation.Version, installation.BuildNumber);
                return $"{identity} [{installation.DiscoverySource}/{installation.MetadataSource}] {installation.ExecutablePath}";
            }));
        AndroidStudioDetails = JoinDetails(evidence, result.Message);
    }

    private void ApplyGit(ToolStatus? git)
    {
        if (git is null)
        {
            GitSummary = "Unavailable";
            GitDetails = "No Git detector result was returned.";
            return;
        }

        GitSummary = git.Installed ? git.Version ?? "Installed" : "Missing";
        GitDetails = JoinDetails(git.Path, git.Message);
    }

    private void ApplyFlutter(FlutterDetectionResult flutter)
    {
        FlutterSummary = flutter.IsSuccess ? JoinSummary(flutter.FlutterVersion, flutter.Channel) : flutter.Status.ToString();
        FlutterDetails = JoinDetails(flutter.FlutterSdkPath ?? flutter.FlutterPath, flutter.Message);
    }

    private void ApplyDart(DartDetectionResult result)
    {
        var primary = result.FlutterBundledCandidate?.IsUsable == true
            ? result.FlutterBundledCandidate
            : result.PathPreferredCandidate?.IsUsable == true
                ? result.PathPreferredCandidate
                : result.Candidates.FirstOrDefault(candidate => candidate.IsUsable);

        DartSummary = result.IsSuccess
            ? JoinSummary(primary?.Version, primary?.IsFlutterBundled == true ? "Flutter bundled" : "PATH/standalone")
            : result.Status switch
            {
                DartSdkDetectionStatus.Missing => "Missing",
                DartSdkDetectionStatus.MetadataMissing => "Version metadata missing",
                DartSdkDetectionStatus.MetadataInvalid => "Version metadata invalid",
                DartSdkDetectionStatus.Cancelled => "Cancelled",
                _ => result.Status.ToString()
            };

        var evidence = result.Candidates.Count == 0
            ? null
            : string.Join(" | ", result.Candidates.Select(candidate =>
            {
                var flags = new List<string>();
                if (candidate.IsFlutterBundled) flags.Add("Flutter");
                if (candidate.IsPathPreferred) flags.Add("PATH preferred");
                if (candidate.IsShadowed) flags.Add("shadowed");
                var flagText = flags.Count == 0 ? string.Empty : $" [{string.Join(", ", flags)}]";
                return $"{candidate.Version ?? "version ?"}{flagText} {candidate.ExecutablePath}";
            }));

        var warning = result.HasFlutterPathMismatch
            ? "Flutter/PATH Dart mismatch detected; no PATH changes were made."
            : result.HasPathConflict
                ? "Multiple Dart executables are present on PATH."
                : null;
        DartDetails = JoinDetails(evidence ?? primary?.ExecutablePath, JoinSummary(warning, result.Message));
    }

    private void ApplyJava(JavaDetectionResult java)
    {
        var preferred = java.PreferredInstallation;
        if (preferred is null)
        {
            JavaSummary = java.Status == JavaDetectionStatus.Missing ? "Missing" : java.Status.ToString();
            JavaDetails = java.Message ?? "No effective Java installation was selected.";
            return;
        }

        JavaSummary = JoinSummary(preferred.Version, preferred.IsJdk ? "JDK" : "JRE");
        JavaDetails = JoinDetails(preferred.JavaHome ?? preferred.ExecutablePath, JoinSummary(preferred.Vendor, java.Message));
    }

    private void ApplyAndroidSdk(AndroidSdkRootDetectionResult result, EnvironmentVariableSnapshot environment)
    {
        AndroidSdkSummary = result.IsSuccess
            ? "Ready"
            : result.Status switch
            {
                AndroidSdkRootDetectionStatus.MissingEffectiveRoot => "Missing",
                AndroidSdkRootDetectionStatus.EffectiveRootInvalid => "Invalid",
                _ => result.Status.ToString()
            };
        AndroidSdkDetails = JoinDetails(
            result.EffectiveCandidate?.NormalizedPath ?? environment.AndroidSdkRoot.EffectiveValue ?? environment.AndroidHome.EffectiveValue,
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

    private void ApplyAvdManager(AndroidAvdManagerDetectionResult result)
    {
        var effective = result.EffectiveCandidate;
        AvdManagerSummary = result.IsSuccess
            ? JoinSummary(effective?.CommandLineToolsRevision, "avdmanager")
            : result.Status switch
            {
                AndroidAvdManagerDetectionStatus.CommandLineToolsUnavailable => "cmdline-tools unavailable",
                AndroidAvdManagerDetectionStatus.AvdManagerMissing => "avdmanager missing",
                _ => result.Status.ToString()
            };
        var evidence = result.Candidates.Count == 0
            ? null
            : string.Join(" | ", result.Candidates.Select(candidate =>
                $"{candidate.CommandLineToolsRevision ?? candidate.Layout.ToString()}: {(candidate.Exists ? "ready" : "missing")}{(candidate.IsEffective ? " effective" : string.Empty)}"));
        AvdManagerDetails = JoinDetails(effective?.AvdManagerPath ?? effective?.InstallationPath, JoinSummary(evidence, result.Message));
    }

    private void ApplyAndroidLicenses(AndroidLicenseDetectionResult result)
    {
        AndroidLicenseSummary = result.Status switch
        {
            AndroidLicenseDetectionStatus.Accepted => "Accepted / Ready",
            AndroidLicenseDetectionStatus.Pending => "Action required",
            AndroidLicenseDetectionStatus.SdkManagerUnavailable => "sdkmanager unavailable",
            AndroidLicenseDetectionStatus.ProbeFailed => "Probe failed",
            AndroidLicenseDetectionStatus.TimedOut => "Probe timed out",
            AndroidLicenseDetectionStatus.Cancelled => "Cancelled",
            AndroidLicenseDetectionStatus.Indeterminate => "Indeterminate",
            _ => result.Status.ToString()
        };
        var files = result.LicenseFiles.Count == 0
            ? "license files: none detected"
            : $"license files: {string.Join(", ", result.LicenseFiles)}";
        var revision = string.IsNullOrWhiteSpace(result.CommandLineToolsRevision)
            ? null
            : $"cmdline-tools {result.CommandLineToolsRevision}";
        AndroidLicenseDetails = JoinDetails(result.SdkManagerPath, JoinSummary(revision, $"{files} • {result.Message}"));
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
        var evidence = result.Platforms.Count == 0
            ? null
            : string.Join(" | ", result.Platforms.Select(platform =>
                $"{platform.PackageId}: {(platform.IsUsable ? "ready" : "partial")}{(string.IsNullOrWhiteSpace(platform.Revision) ? string.Empty : $" rev {platform.Revision}")}{(platform.IsPreview && !string.IsNullOrWhiteSpace(platform.CodeName) ? $" {platform.CodeName}" : string.Empty)}"));
        AndroidPlatformsDetails = JoinDetails(evidence, result.Message);
    }

    private void ApplyAndroidBuildTools(AndroidBuildToolsDetectionResult result)
    {
        if (result.IsSuccess)
        {
            var latest = result.InstalledVersions.FirstOrDefault();
            var usableCount = result.Packages.Count(package => package.IsUsable);
            AndroidBuildToolsSummary = string.IsNullOrWhiteSpace(latest)
                ? $"{usableCount} usable"
                : $"{latest} • {usableCount} usable";
        }
        else
        {
            AndroidBuildToolsSummary = result.Status switch
            {
                AndroidBuildToolsDetectionStatus.BuildToolsDirectoryMissing => "build-tools missing",
                AndroidBuildToolsDetectionStatus.NoBuildToolsInstalled => "None installed",
                AndroidBuildToolsDetectionStatus.PartialInstallationsOnly => "Partial/broken only",
                AndroidBuildToolsDetectionStatus.AndroidSdkRootUnavailable => "SDK unavailable",
                AndroidBuildToolsDetectionStatus.InspectionFailed => "Inspection failed",
                _ => result.Status.ToString()
            };
        }

        var evidence = result.Packages.Count == 0
            ? null
            : string.Join(" | ", result.Packages.Select(package =>
            {
                var missing = new List<string>();
                if (!package.Aapt2Exists) missing.Add("aapt2");
                if (!package.ZipAlignExists) missing.Add("zipalign");
                if (!package.D8Exists) missing.Add("d8");
                if (!package.ApkSignerExists) missing.Add("apksigner");
                return $"{(string.IsNullOrWhiteSpace(package.Revision) ? package.DirectoryName : package.Revision)}: {(package.IsUsable ? "ready" : "partial")}{(missing.Count == 0 ? string.Empty : $" missing {string.Join(",", missing)}")}";
            }));
        AndroidBuildToolsDetails = JoinDetails(evidence, result.Message);
    }

    private void ApplyAndroidEmulator(AndroidEmulatorDetectionResult result)
    {
        AndroidEmulatorSummary = result.IsSuccess
            ? string.IsNullOrWhiteSpace(result.Version) ? "Ready" : result.Version
            : result.Status switch
            {
                AndroidEmulatorDetectionStatus.EmulatorDirectoryMissing => "Package missing",
                AndroidEmulatorDetectionStatus.EmulatorMissing => "Binary missing",
                AndroidEmulatorDetectionStatus.AndroidSdkRootUnavailable => "SDK unavailable",
                AndroidEmulatorDetectionStatus.TimedOut => "Probe timed out",
                AndroidEmulatorDetectionStatus.ProbeFailed => "Probe failed",
                AndroidEmulatorDetectionStatus.VersionUnavailable => "Version unavailable",
                AndroidEmulatorDetectionStatus.Cancelled => "Cancelled",
                _ => result.Status.ToString()
            };
        AndroidEmulatorDetails = JoinDetails(result.EmulatorPath ?? result.EmulatorDirectory, result.Message);
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
            return string.IsNullOrWhiteSpace(secondary) ? "Unknown" : secondary;
        return string.IsNullOrWhiteSpace(secondary) ? primary : $"{primary} • {secondary}";
    }

    private static string JoinDetails(string? path, string? message)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.IsNullOrWhiteSpace(message) ? "No details available." : message;
        return string.IsNullOrWhiteSpace(message) ? path : $"{path} • {message}";
    }

    public void Dispose()
    {
        _scanCancellation?.Cancel();
        _scanCancellation?.Dispose();
        _scanCancellation = null;
    }
}
