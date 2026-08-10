using FlutterBuildDoctor.Android.Devices;
using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.UnitTests.Android;

public sealed class AndroidDeviceManagerTests
{
    [Fact]
    public async Task ListDevicesAsync_ExecutesAdbLongInventoryAndParsesResult()
    {
        var runner = new SequencedRunner(Success(
            "List of devices attached",
            "emulator-5554 device product:sdk_gphone64_x86_64 model:Pixel_9 device:emu transport_id:1"));
        var manager = Manager(runner);

        var inventory = await manager.ListDevicesAsync("adb");

        Assert.Equal(new[] { "devices", "-l" }, runner.LastRequest!.Arguments);
        Assert.Single(inventory.Devices);
        Assert.Equal(AndroidDeviceState.Online, inventory.Devices[0].State);
    }

    [Fact]
    public void LaunchEmulator_UsesDetachedTypedProcess()
    {
        var launcher = new RecordingLauncher();
        var manager = Manager(new SequencedRunner(Success()), launcher);

        var result = manager.LaunchEmulator("emulator", "Pixel_9_API_36");

        Assert.True(result.Started);
        Assert.Equal(new[] { "-avd", "Pixel_9_API_36" }, launcher.LastRequest!.Arguments);
    }

    [Fact]
    public async Task WaitForDeviceAsync_TargetsExactSerial()
    {
        var runner = new SequencedRunner(Success());
        var manager = Manager(runner);

        await manager.WaitForDeviceAsync("adb", "emulator-5554");

        Assert.Equal(new[] { "-s", "emulator-5554", "wait-for-device" }, runner.LastRequest!.Arguments);
    }

    [Fact]
    public async Task WaitForBootCompletedAsync_StopsWhenBootPropertyBecomesOne()
    {
        var runner = new SequencedRunner(Success("0"), Success("1"));
        var manager = Manager(runner);

        var result = await manager.WaitForBootCompletedAsync(
            "adb",
            "emulator-5554",
            maxAttempts: 3,
            pollInterval: TimeSpan.Zero);

        Assert.True(result.IsReady);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(2, runner.CallCount);
        Assert.Equal(
            new[] { "-s", "emulator-5554", "shell", "getprop", "sys.boot_completed" },
            runner.LastRequest!.Arguments);
    }

    [Fact]
    public async Task StopEmulatorAsync_UsesEmuKill()
    {
        var runner = new SequencedRunner(Success());
        var manager = Manager(runner);

        await manager.StopEmulatorAsync("adb", "emulator-5554");

        Assert.Equal(new[] { "-s", "emulator-5554", "emu", "kill" }, runner.LastRequest!.Arguments);
    }

    [Fact]
    public async Task InstallApkAsync_UsesExplicitReplaceAndDowngradePolicy()
    {
        var apk = Path.Combine(Path.GetTempPath(), $"fbd-install-{Guid.NewGuid():N}.apk");
        await File.WriteAllTextAsync(apk, "apk");
        try
        {
            var runner = new SequencedRunner(Success("Success"));
            var manager = Manager(runner);

            var result = await manager.InstallApkAsync(
                "adb",
                "R58M123456",
                apk,
                new ApkInstallPolicy(ReplaceExisting: true, AllowDowngrade: false));

            Assert.True(result.IsSuccess);
            Assert.Equal("-s", runner.LastRequest!.Arguments[0]);
            Assert.Equal("R58M123456", runner.LastRequest.Arguments[1]);
            Assert.Contains("install", runner.LastRequest.Arguments);
            Assert.Contains("-r", runner.LastRequest.Arguments);
            Assert.DoesNotContain("-d", runner.LastRequest.Arguments);
            Assert.Equal(Path.GetFullPath(apk), runner.LastRequest.Arguments[^1]);
        }
        finally
        {
            File.Delete(apk);
        }
    }

    [Fact]
    public async Task StreamLogcatAsync_IsCancellableAndRequestsBoundedCapture()
    {
        var runner = new SequencedRunner(Result(ProcessExecutionStatus.Cancelled, null, "Process was cancelled."));
        var manager = Manager(runner);
        using var source = new CancellationTokenSource();

        var result = await manager.StreamLogcatAsync(
            "adb",
            "emulator-5554",
            maxCapturedLines: 500,
            cancellationToken: source.Token);

        Assert.Equal(ProcessExecutionStatus.Cancelled, result.Status);
        Assert.Equal(new[] { "-s", "emulator-5554", "logcat" }, runner.LastRequest!.Arguments);
        Assert.Equal(500, runner.LastRequest.MaxCapturedOutputLines);
        Assert.Null(runner.LastRequest.Timeout);
        Assert.Equal(source.Token, runner.LastCancellationToken);
    }

    [Fact]
    public async Task ListAvdsAsync_UsesEmulatorListCommand()
    {
        var runner = new SequencedRunner(Success("Pixel_8_API_35", "Pixel_9_API_36"));
        var manager = Manager(runner);

        var inventory = await manager.ListAvdsAsync("emulator");

        Assert.Equal(new[] { "-list-avds" }, runner.LastRequest!.Arguments);
        Assert.Equal(2, inventory.Avds.Count);
    }

    private static AndroidDeviceManager Manager(
        IProcessRunner runner,
        IDetachedProcessLauncher? launcher = null)
        => new(
            runner,
            launcher ?? new RecordingLauncher(),
            new AdbDevicesParser(),
            new AvdListParser());

    private static ProcessResult Success(params string[] lines)
        => Result(ProcessExecutionStatus.Succeeded, 0, null, lines);

    private static ProcessResult Result(
        ProcessExecutionStatus status,
        int? exitCode,
        string? failureReason,
        params string[] lines)
    {
        var now = DateTimeOffset.UtcNow;
        return new ProcessResult(
            status,
            exitCode,
            now,
            now.AddMilliseconds(10),
            lines.Select(line => new ProcessOutputLine(now, ProcessStream.StdOut, line)).ToArray(),
            "adb",
            failureReason);
    }

    private sealed class SequencedRunner : IProcessRunner
    {
        private readonly Queue<ProcessResult> _results;

        public SequencedRunner(params ProcessResult[] results)
            => _results = new Queue<ProcessResult>(results);

        public int CallCount { get; private set; }
        public ProcessRequest? LastRequest { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class RecordingLauncher : IDetachedProcessLauncher
    {
        public ProcessRequest? LastRequest { get; private set; }

        public ProcessLaunchResult Launch(ProcessRequest request)
        {
            LastRequest = request;
            return new ProcessLaunchResult(true, 12345, "emulator -avd Pixel_9_API_36");
        }
    }
}
