using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Android.Devices;

public enum AndroidDeviceState
{
    Online = 0,
    Offline,
    Unauthorized,
    Recovery,
    Bootloader,
    Unknown
}

public sealed record AndroidDeviceRecord(
    string Serial,
    AndroidDeviceState State,
    string? Product,
    string? Model,
    string? Device,
    string? TransportId,
    IReadOnlyDictionary<string, string> Properties,
    string RawLine);

public sealed record AndroidDeviceMetadata(
    string Serial,
    string DisplayName,
    AndroidDeviceState State,
    bool IsEmulator,
    string? Product,
    string? Device,
    string? TransportId);

public sealed record AndroidVirtualDevice(string Name);

public sealed record AndroidDeviceInventory(
    ProcessExecutionStatus Status,
    IReadOnlyList<AndroidDeviceRecord> Devices,
    ProcessResult ProcessResult);

public sealed record AndroidAvdInventory(
    ProcessExecutionStatus Status,
    IReadOnlyList<AndroidVirtualDevice> Avds,
    ProcessResult ProcessResult);

public sealed record AndroidBootWaitResult(
    bool IsReady,
    int Attempts,
    ProcessExecutionStatus LastStatus,
    string Message);

public sealed record ApkInstallPolicy(
    bool ReplaceExisting = false,
    bool AllowDowngrade = false);

public interface IAdbDevicesParser
{
    IReadOnlyList<AndroidDeviceRecord> Parse(string? output);
}

public interface IAvdListParser
{
    IReadOnlyList<AndroidVirtualDevice> Parse(string? output);
}

public interface IAndroidDeviceMetadataProjector
{
    AndroidDeviceMetadata Project(AndroidDeviceRecord device);
}

public interface IAndroidDeviceManager
{
    Task<AndroidDeviceInventory> ListDevicesAsync(
        string adbExecutable,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default);

    Task<AndroidAvdInventory> ListAvdsAsync(
        string emulatorExecutable,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default);

    ProcessLaunchResult LaunchEmulator(
        string emulatorExecutable,
        string avdName,
        string? workingDirectory = null);

    Task<ProcessResult> WaitForDeviceAsync(
        string adbExecutable,
        string serial,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default);

    Task<AndroidBootWaitResult> WaitForBootCompletedAsync(
        string adbExecutable,
        string serial,
        int maxAttempts = 60,
        TimeSpan? pollInterval = null,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default);

    Task<ProcessResult> StopEmulatorAsync(
        string adbExecutable,
        string serial,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default);

    Task<ProcessResult> InstallApkAsync(
        string adbExecutable,
        string serial,
        string apkPath,
        ApkInstallPolicy policy,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default);

    Task<ProcessResult> StreamLogcatAsync(
        string adbExecutable,
        string serial,
        IProgress<ProcessOutputLine>? progress = null,
        int maxCapturedLines = 2000,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default);
}
