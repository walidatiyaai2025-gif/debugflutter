using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Android.Devices;

public sealed class AndroidDeviceManager : IAndroidDeviceManager
{
    private readonly IProcessRunner _processRunner;
    private readonly IDetachedProcessLauncher _processLauncher;
    private readonly IAdbDevicesParser _adbDevicesParser;
    private readonly IAvdListParser _avdListParser;

    public AndroidDeviceManager(
        IProcessRunner processRunner,
        IDetachedProcessLauncher processLauncher,
        IAdbDevicesParser adbDevicesParser,
        IAvdListParser avdListParser)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _processLauncher = processLauncher ?? throw new ArgumentNullException(nameof(processLauncher));
        _adbDevicesParser = adbDevicesParser ?? throw new ArgumentNullException(nameof(adbDevicesParser));
        _avdListParser = avdListParser ?? throw new ArgumentNullException(nameof(avdListParser));
    }

    public async Task<AndroidDeviceInventory> ListDevicesAsync(
        string adbExecutable,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var request = Request(
            adbExecutable,
            new[] { "devices", "-l" },
            workingDirectory,
            TimeSpan.FromSeconds(30),
            "adb devices -l");
        var result = await _processRunner.RunAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);
        var output = string.Join(Environment.NewLine, result.Output.Select(static line => line.Text));
        return new AndroidDeviceInventory(result.Status, _adbDevicesParser.Parse(output), result);
    }

    public async Task<AndroidAvdInventory> ListAvdsAsync(
        string emulatorExecutable,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var request = Request(
            emulatorExecutable,
            new[] { "-list-avds" },
            workingDirectory,
            TimeSpan.FromSeconds(30),
            "emulator -list-avds");
        var result = await _processRunner.RunAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);
        var output = string.Join(Environment.NewLine, result.Output.Select(static line => line.Text));
        return new AndroidAvdInventory(result.Status, _avdListParser.Parse(output), result);
    }

    public ProcessLaunchResult LaunchEmulator(
        string emulatorExecutable,
        string avdName,
        string? workingDirectory = null)
    {
        var request = Request(
            emulatorExecutable,
            new[] { "-avd", Safe(avdName, nameof(avdName)) },
            workingDirectory,
            null,
            "emulator launch");
        return _processLauncher.Launch(request);
    }

    public Task<ProcessResult> WaitForDeviceAsync(
        string adbExecutable,
        string serial,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var request = AdbRequest(
            adbExecutable,
            serial,
            new[] { "wait-for-device" },
            workingDirectory,
            TimeSpan.FromMinutes(2),
            "adb wait-for-device");
        return _processRunner.RunAsync(request, cancellationToken: cancellationToken);
    }

    public async Task<AndroidBootWaitResult> WaitForBootCompletedAsync(
        string adbExecutable,
        string serial,
        int maxAttempts = 60,
        TimeSpan? pollInterval = null,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        if (maxAttempts is < 1 or > 600)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "Boot polling attempts must be between 1 and 600.");
        var interval = pollInterval ?? TimeSpan.FromSeconds(2);
        if (interval < TimeSpan.Zero || interval > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(pollInterval));

        ProcessExecutionStatus lastStatus = ProcessExecutionStatus.Created;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = AdbRequest(
                adbExecutable,
                serial,
                new[] { "shell", "getprop", "sys.boot_completed" },
                workingDirectory,
                TimeSpan.FromSeconds(10),
                "adb boot readiness");
            var result = await _processRunner.RunAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);
            lastStatus = result.Status;

            if (result.Output.Any(line => string.Equals(line.Text.Trim(), "1", StringComparison.Ordinal)))
                return new AndroidBootWaitResult(true, attempt, result.Status, "Android boot completed.");

            if (result.Status == ProcessExecutionStatus.Cancelled)
                return new AndroidBootWaitResult(false, attempt, result.Status, "Boot readiness wait was cancelled.");

            if (attempt < maxAttempts && interval > TimeSpan.Zero)
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
        }

        return new AndroidBootWaitResult(
            false,
            maxAttempts,
            lastStatus,
            "Android did not report sys.boot_completed=1 within the bounded polling attempts.");
    }

    public Task<ProcessResult> StopEmulatorAsync(
        string adbExecutable,
        string serial,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var request = AdbRequest(
            adbExecutable,
            serial,
            new[] { "emu", "kill" },
            workingDirectory,
            TimeSpan.FromSeconds(30),
            "adb emulator stop");
        return _processRunner.RunAsync(request, cancellationToken: cancellationToken);
    }

    public Task<ProcessResult> InstallApkAsync(
        string adbExecutable,
        string serial,
        string apkPath,
        ApkInstallPolicy policy,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var fullPath = Path.GetFullPath(Safe(apkPath, nameof(apkPath)));
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("APK to install was not found.", fullPath);

        var arguments = new List<string> { "install" };
        if (policy.ReplaceExisting) arguments.Add("-r");
        if (policy.AllowDowngrade) arguments.Add("-d");
        arguments.Add(fullPath);

        var request = AdbRequest(
            adbExecutable,
            serial,
            arguments,
            workingDirectory,
            TimeSpan.FromMinutes(3),
            "adb install");
        return _processRunner.RunAsync(request, cancellationToken: cancellationToken);
    }

    public Task<ProcessResult> StreamLogcatAsync(
        string adbExecutable,
        string serial,
        IProgress<ProcessOutputLine>? progress = null,
        int maxCapturedLines = 2000,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        if (maxCapturedLines is < 100 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(maxCapturedLines), "Logcat capture must retain between 100 and 10,000 lines.");

        var request = AdbRequest(
            adbExecutable,
            serial,
            new[] { "logcat" },
            workingDirectory,
            null,
            "adb logcat") with
        {
            MaxCapturedOutputLines = maxCapturedLines
        };
        return _processRunner.RunAsync(request, progress, cancellationToken);
    }

    private static ProcessRequest AdbRequest(
        string adbExecutable,
        string serial,
        IReadOnlyList<string> commandArguments,
        string? workingDirectory,
        TimeSpan? timeout,
        string displayName)
    {
        var arguments = new List<string> { "-s", Safe(serial, nameof(serial)) };
        arguments.AddRange(commandArguments);
        return Request(adbExecutable, arguments, workingDirectory, timeout, displayName);
    }

    private static ProcessRequest Request(
        string executable,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        TimeSpan? timeout,
        string displayName)
        => new(
            Safe(executable, nameof(executable)),
            arguments,
            workingDirectory,
            Timeout: timeout,
            DisplayName: displayName);

    private static string Safe(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value is required.", parameterName);
        if (value.Any(char.IsControl))
            throw new ArgumentException("Control characters are not allowed.", parameterName);
        return value;
    }
}
