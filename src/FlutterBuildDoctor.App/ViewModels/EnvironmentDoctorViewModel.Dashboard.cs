using CommunityToolkit.Mvvm.ComponentModel;

namespace FlutterBuildDoctor.App.ViewModels;

public sealed partial class EnvironmentDoctorViewModel
{
    [ObservableProperty]
    private string _overallReadinessSummary = "Not scanned";

    [ObservableProperty]
    private int _readyComponentCount;

    [ObservableProperty]
    private int _attentionComponentCount;

    [ObservableProperty]
    private int _totalComponentCount = 13;

    [ObservableProperty]
    private string _readinessDetails = "Run Environment Doctor to build the current environment snapshot.";

    partial void OnHasScannedChanged(bool value)
    {
        if (value)
        {
            RefreshReadinessDashboard();
        }
    }

    partial void OnLastScannedAtChanged(DateTimeOffset? value)
    {
        if (value.HasValue && HasScanned)
        {
            RefreshReadinessDashboard();
        }
    }

    private void RefreshReadinessDashboard()
    {
        var states = new[]
        {
            IsReady(WindowsSummary, "Not Windows", "Unavailable", "Not scanned"),
            IsReady(AndroidStudioSummary, "Not Windows", "Not found", "Inspection failed", "Not scanned"),
            IsReady(GitSummary, "Missing", "Unavailable", "Not scanned"),
            IsReady(FlutterSummary, "Missing", "Cancelled", "Not scanned"),
            IsReady(DartSummary, "Missing", "Version metadata missing", "Version metadata invalid", "Cancelled", "Not scanned"),
            IsReady(JavaSummary, "Missing", "Cancelled", "Not scanned"),
            string.Equals(AndroidSdkSummary, "Ready", StringComparison.OrdinalIgnoreCase),
            IsReady(CommandLineToolsSummary, "Missing", "sdkmanager missing", "Metadata invalid", "SDK unavailable", "Not scanned"),
            IsReady(AvdManagerSummary, "cmdline-tools unavailable", "avdmanager missing", "Not scanned"),
            string.Equals(AndroidLicenseSummary, "Accepted / Ready", StringComparison.OrdinalIgnoreCase),
            IsReady(AdbSummary, "platform-tools missing", "ADB missing", "SDK unavailable", "Probe timed out", "Probe failed", "Version parse failed", "Cancelled", "Not scanned"),
            IsReady(AndroidEmulatorSummary, "Package missing", "Binary missing", "SDK unavailable", "Probe timed out", "Probe failed", "Version unavailable", "Cancelled", "Not scanned"),
            IsReady(AndroidPlatformsSummary, "platforms missing", "None installed", "Partial/broken only", "SDK unavailable", "Inspection failed", "Not scanned") &&
            IsReady(AndroidBuildToolsSummary, "build-tools missing", "None installed", "Partial/broken only", "SDK unavailable", "Inspection failed", "Not scanned")
        };

        TotalComponentCount = states.Length;
        ReadyComponentCount = states.Count(ready => ready);
        AttentionComponentCount = TotalComponentCount - ReadyComponentCount;
        OverallReadinessSummary = AttentionComponentCount == 0
            ? "Environment ready"
            : $"{ReadyComponentCount}/{TotalComponentCount} checks ready";
        ReadinessDetails = AttentionComponentCount == 0
            ? "All dashboard readiness groups passed the latest scan."
            : $"{AttentionComponentCount} readiness group(s) need attention. Review the cards below for exact evidence.";
    }

    private static bool IsReady(string? value, params string[] negativeStates)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return !negativeStates.Any(state =>
            string.Equals(value, state, StringComparison.OrdinalIgnoreCase));
    }
}
