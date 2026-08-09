using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlutterBuildDoctor.App.EnvironmentSnapshots;
using FlutterBuildDoctor.Application.Environment;

namespace FlutterBuildDoctor.App.ViewModels;

public sealed partial class EnvironmentDoctorViewModel : ObservableObject
{
    private readonly IEnvironmentSnapshotService _snapshotService;
    private bool _initialized;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "Environment Doctor has not scanned this machine yet.";

    [ObservableProperty]
    private string _capturedAtText = "Not scanned";

    [ObservableProperty]
    private string _captureDurationText = "—";

    [ObservableProperty]
    private int _readyCount;

    [ObservableProperty]
    private int _attentionCount;

    public EnvironmentDoctorViewModel(IEnvironmentSnapshotService snapshotService)
    {
        _snapshotService = snapshotService ?? throw new ArgumentNullException(nameof(snapshotService));
    }

    public ObservableCollection<EnvironmentComponentViewModel> Components { get; } = new();

    public bool IsLoaded => _initialized;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized || IsLoading)
        {
            return;
        }

        await CaptureAndApplyAsync(isRefresh: false, cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (IsLoading)
        {
            return;
        }

        await CaptureAndApplyAsync(isRefresh: true, CancellationToken.None).ConfigureAwait(true);
    }

    private bool CanRefresh() => !IsLoading;

    partial void OnIsLoadingChanged(bool value)
        => RefreshCommand.NotifyCanExecuteChanged();

    private async Task CaptureAndApplyAsync(bool isRefresh, CancellationToken cancellationToken)
    {
        if (IsLoading)
        {
            return;
        }

        var hadLoadedSnapshot = _initialized;
        IsLoading = true;
        StatusMessage = isRefresh
            ? "Refreshing Windows, Flutter, Java, and Android toolchains..."
            : "Scanning Windows, Flutter, Java, and Android toolchains...";

        try
        {
            var snapshot = await _snapshotService.CaptureAsync(cancellationToken).ConfigureAwait(true);
            ApplySnapshot(snapshot);

            if (!_initialized)
            {
                _initialized = true;
                OnPropertyChanged(nameof(IsLoaded));
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = hadLoadedSnapshot
                ? "Environment refresh cancelled. The last successful scan remains displayed."
                : "Environment scan cancelled.";
        }
        catch (Exception exception)
        {
            if (hadLoadedSnapshot)
            {
                StatusMessage = $"Environment refresh failed: {exception.Message} The last successful scan remains displayed.";
            }
            else
            {
                Components.Clear();
                ReadyCount = 0;
                AttentionCount = 0;
                CapturedAtText = "Scan failed";
                CaptureDurationText = "—";
                StatusMessage = $"Environment scan failed: {exception.Message}";
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplySnapshot(EnvironmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        Components.Clear();
        foreach (var component in BuildComponents(snapshot))
        {
            Components.Add(component);
        }

        ReadyCount = Components.Count(component => component.IsReady);
        AttentionCount = Components.Count - ReadyCount;
        CapturedAtText = snapshot.CompletedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        CaptureDurationText = $"{snapshot.CaptureDuration.TotalSeconds:0.0}s";
        StatusMessage = AttentionCount == 0
            ? $"Environment ready: all {ReadyCount} components are healthy."
            : $"Environment scan complete: {ReadyCount} ready, {AttentionCount} need attention.";
    }

    private static IReadOnlyList<EnvironmentComponentViewModel> BuildComponents(EnvironmentSnapshot snapshot)
    {
        var components = new List<EnvironmentComponentViewModel>();

        components.Add(Card(
            "Windows",
            snapshot.Windows.IsSuccess,
            snapshot.Windows.Status.ToString(),
            snapshot.Windows.Description ?? "Windows host",
            Join(snapshot.Windows.Version, snapshot.Windows.OsArchitecture),
            "Run Flutter Build Doctor on a supported Windows installation."));

        var variablesReadable = snapshot.EnvironmentVariables.Variables.All(variable =>
            variable.Process.Status != VariableReadStatus.Unavailable &&
            variable.User.Status != VariableReadStatus.Unavailable &&
            variable.Machine.Status != VariableReadStatus.Unavailable);
        var androidEnvironmentRoot = snapshot.EnvironmentVariables.AndroidSdkRoot.EffectiveValue
                                     ?? snapshot.EnvironmentVariables.AndroidHome.EffectiveValue;
        components.Add(Card(
            "Environment Variables",
            variablesReadable,
            variablesReadable ? "Captured" : "Unavailable scope",
            $"JAVA_HOME: {Display(snapshot.EnvironmentVariables.JavaHome.EffectiveValue)} • Android SDK: {Display(androidEnvironmentRoot)}",
            "PATH, JAVA_HOME, ANDROID_HOME, ANDROID_SDK_ROOT",
            "Check environment-variable access and resolve conflicting tool paths."));

        components.Add(Card(
            "Flutter SDK",
            snapshot.Flutter.IsSuccess,
            snapshot.Flutter.Status.ToString(),
            snapshot.Flutter.FlutterSdkPath ?? snapshot.Flutter.FlutterPath,
            Join(snapshot.Flutter.FlutterVersion, snapshot.Flutter.Channel),
            "Install or repair Flutter SDK discovery, then rescan."));

        var dart = snapshot.Dart.FlutterBundledCandidate
                   ?? snapshot.Dart.PathPreferredCandidate
                   ?? snapshot.Dart.Candidates.FirstOrDefault();
        components.Add(Card(
            "Dart SDK",
            snapshot.Dart.IsSuccess,
            snapshot.Dart.Status.ToString(),
            dart?.SdkRoot ?? dart?.ExecutablePath,
            dart?.Version,
            "Repair Flutter/Dart SDK alignment or PATH precedence, then rescan."));

        var java = snapshot.Java.PreferredInstallation ?? snapshot.Java.Installations.FirstOrDefault();
        components.Add(Card(
            "Java / JDK",
            snapshot.Java.IsSuccess,
            snapshot.Java.Status.ToString(),
            java?.JavaHome ?? java?.ExecutablePath,
            Join(java?.Version, java?.Vendor, java?.Architecture),
            "Install or select a compatible JDK and correct PATH/JAVA_HOME."));

        components.Add(Card(
            "Android SDK",
            snapshot.AndroidSdk.IsSuccess,
            snapshot.AndroidSdk.Status.ToString(),
            snapshot.AndroidSdk.EffectiveCandidate?.NormalizedPath,
            snapshot.AndroidSdk.EffectiveCandidate?.HasRecognizedSdkLayout == true ? "Validated SDK layout" : null,
            "Point ANDROID_SDK_ROOT/ANDROID_HOME to a valid Android SDK."));

        components.Add(Card(
            "Command-line Tools",
            snapshot.AndroidCommandLineTools.IsSuccess,
            snapshot.AndroidCommandLineTools.Status.ToString(),
            snapshot.AndroidCommandLineTools.EffectiveCandidate?.SdkManagerPath
                ?? snapshot.AndroidCommandLineTools.EffectiveCandidate?.InstallationPath,
            snapshot.AndroidCommandLineTools.EffectiveCandidate?.Revision,
            "Install a complete Android SDK command-line tools package."));

        components.Add(Card(
            "ADB / Platform Tools",
            snapshot.Adb.IsSuccess,
            snapshot.Adb.Status.ToString(),
            snapshot.Adb.AdbPath ?? snapshot.Adb.PlatformToolsPath,
            Join(snapshot.Adb.PlatformToolsVersion, snapshot.Adb.AdbProtocolVersion),
            "Install or repair Android platform-tools so adb can be probed."));

        components.Add(Card(
            "Android Platforms",
            snapshot.AndroidPlatforms.IsSuccess,
            snapshot.AndroidPlatforms.Status.ToString(),
            snapshot.AndroidPlatforms.Platforms.FirstOrDefault()?.InstallationPath,
            snapshot.AndroidPlatforms.InstalledApiLevels.Count == 0
                ? null
                : string.Join(", ", snapshot.AndroidPlatforms.InstalledApiLevels.Select(level => $"API {level}")),
            "Install the Android platform package required by the project."));

        components.Add(Card(
            "Android Build Tools",
            snapshot.AndroidBuildTools.IsSuccess,
            snapshot.AndroidBuildTools.Status.ToString(),
            snapshot.AndroidBuildTools.Packages.FirstOrDefault()?.InstallationPath,
            snapshot.AndroidBuildTools.InstalledVersions.Count == 0
                ? null
                : string.Join(", ", snapshot.AndroidBuildTools.InstalledVersions),
            "Install a complete Android build-tools package."));

        components.Add(Card(
            "Android Emulator",
            snapshot.Emulator.IsSuccess,
            snapshot.Emulator.Status.ToString(),
            snapshot.Emulator.EmulatorPath ?? snapshot.Emulator.EmulatorDirectory,
            snapshot.Emulator.Version,
            "Install or repair the Android Emulator package when emulation is required."));

        components.Add(Card(
            "AVD Manager",
            snapshot.AvdManager.IsSuccess,
            snapshot.AvdManager.Status.ToString(),
            snapshot.AvdManager.EffectiveCandidate?.AvdManagerPath
                ?? snapshot.AvdManager.EffectiveCandidate?.InstallationPath,
            snapshot.AvdManager.EffectiveCandidate?.CommandLineToolsRevision,
            "Install command-line tools that provide avdmanager."));

        components.Add(Card(
            "Android Licenses",
            snapshot.AndroidLicenses.IsReady,
            snapshot.AndroidLicenses.Status.ToString(),
            string.IsNullOrWhiteSpace(snapshot.AndroidLicenses.AndroidSdkRoot)
                ? null
                : $"{snapshot.AndroidLicenses.AndroidSdkRoot}\\licenses",
            snapshot.AndroidLicenses.Status.ToString(),
            "Accept pending Android SDK licenses before building."));

        var studio = snapshot.AndroidStudio.Installations.FirstOrDefault();
        components.Add(Card(
            "Android Studio",
            snapshot.AndroidStudio.IsSuccess,
            snapshot.AndroidStudio.Status.ToString(),
            studio?.ExecutablePath ?? studio?.InstallationPath,
            Join(studio?.Version, studio?.BuildNumber),
            "Install Android Studio or repair its installation metadata."));

        return components;
    }

    private static EnvironmentComponentViewModel Card(
        string name,
        bool ready,
        string statusDetail,
        string? path,
        string? version,
        string attentionAction)
        => new(
            name,
            ready ? EnvironmentComponentState.Ready : EnvironmentComponentState.Attention,
            ready ? "Ready" : "Needs attention",
            statusDetail,
            Display(path),
            Display(version),
            ready ? "No action required." : attentionAction);

    private static string Display(string? value)
        => string.IsNullOrWhiteSpace(value) ? "Not detected" : value.Trim();

    private static string Join(params string?[] parts)
    {
        var values = parts
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim())
            .ToArray();

        return values.Length == 0 ? "Not detected" : string.Join(" • ", values);
    }
}
