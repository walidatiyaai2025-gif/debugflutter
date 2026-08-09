using FlutterBuildDoctor.Android.Detection;
using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.UnitTests.Android;

public sealed class AndroidLicenseDetectorTests
{
    [Fact]
    public async Task DetectAsync_AllLicensesAccepted_ReturnsReadyAndForcesClosedInput()
    {
        using var fixture = new LicenseFixture(withSdkManager: true);
        fixture.CreateLicense("android-sdk-license");
        fixture.CreateLicense("android-sdk-preview-license");
        var runner = new StubProcessRunner(Result(
            ProcessExecutionStatus.Succeeded,
            0,
            Line(ProcessStream.StdOut, "All SDK package licenses accepted.")));

        var result = await new AndroidLicenseDetector(runner).DetectAsync(fixture.CommandLineResult());

        Assert.Equal(AndroidLicenseDetectionStatus.Accepted, result.Status);
        Assert.True(result.IsReady);
        Assert.Equal(new[] { "android-sdk-license", "android-sdk-preview-license" }, result.LicenseFiles);
        Assert.Contains("All SDK package licenses accepted", result.RawOutput, StringComparison.OrdinalIgnoreCase);
        AssertSafeProbe(Assert.Single(runner.Requests), fixture.SdkManagerPath!);
    }

    [Fact]
    public async Task DetectAsync_PendingPrompt_ReturnsPendingEvenWhenSdkManagerExitsNonZero()
    {
        using var fixture = new LicenseFixture(withSdkManager: true);
        var runner = new StubProcessRunner(Result(
            ProcessExecutionStatus.Failed,
            1,
            Line(ProcessStream.StdOut, "Review licenses that have not been accepted (y/N)?"),
            Line(ProcessStream.StdOut, "Accept? (y/N):")));

        var result = await new AndroidLicenseDetector(runner).DetectAsync(fixture.CommandLineResult());

        Assert.Equal(AndroidLicenseDetectionStatus.Pending, result.Status);
        Assert.False(result.IsReady);
        Assert.Contains("require review/acceptance", result.Message, StringComparison.OrdinalIgnoreCase);
        AssertSafeProbe(Assert.Single(runner.Requests), fixture.SdkManagerPath!);
    }

    [Fact]
    public async Task DetectAsync_TimeoutAfterPendingPrompt_ReturnsPendingWithBoundedEvidence()
    {
        using var fixture = new LicenseFixture(withSdkManager: true);
        var runner = new StubProcessRunner(Result(
            ProcessExecutionStatus.TimedOut,
            null,
            Line(ProcessStream.StdErr, "Accept? [y/N]")));

        var result = await new AndroidLicenseDetector(runner).DetectAsync(fixture.CommandLineResult());

        Assert.Equal(AndroidLicenseDetectionStatus.Pending, result.Status);
        Assert.Contains("bounded probe was stopped", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.ProbeResult);
    }

    [Fact]
    public async Task DetectAsync_TimeoutWithoutDecisiveOutput_ReturnsTimedOut()
    {
        using var fixture = new LicenseFixture(withSdkManager: true);
        var runner = new StubProcessRunner(Result(
            ProcessExecutionStatus.TimedOut,
            null,
            Line(ProcessStream.StdErr, "Loading package information...")));

        var result = await new AndroidLicenseDetector(runner).DetectAsync(fixture.CommandLineResult());

        Assert.Equal(AndroidLicenseDetectionStatus.TimedOut, result.Status);
        Assert.Contains("No acceptance input was provided", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DetectAsync_FailedProbeWithoutLicenseEvidence_ReturnsProbeFailed()
    {
        using var fixture = new LicenseFixture(withSdkManager: true);
        var runner = new StubProcessRunner(Result(
            ProcessExecutionStatus.Failed,
            1,
            Line(ProcessStream.StdErr, "Java version is too old")));

        var result = await new AndroidLicenseDetector(runner).DetectAsync(fixture.CommandLineResult());

        Assert.Equal(AndroidLicenseDetectionStatus.ProbeFailed, result.Status);
        Assert.Contains("Java version is too old", result.RawOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetectAsync_SuccessWithoutKnownStatus_ReturnsIndeterminate()
    {
        using var fixture = new LicenseFixture(withSdkManager: true);
        var runner = new StubProcessRunner(Result(
            ProcessExecutionStatus.Succeeded,
            0,
            Line(ProcessStream.StdOut, "Unknown future sdkmanager wording")));

        var result = await new AndroidLicenseDetector(runner).DetectAsync(fixture.CommandLineResult());

        Assert.Equal(AndroidLicenseDetectionStatus.Indeterminate, result.Status);
        Assert.Contains("Unknown future sdkmanager wording", result.RawOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetectAsync_ProcessCancelled_ReturnsCancelled()
    {
        using var fixture = new LicenseFixture(withSdkManager: true);
        var runner = new StubProcessRunner(Result(ProcessExecutionStatus.Cancelled, null));

        var result = await new AndroidLicenseDetector(runner).DetectAsync(fixture.CommandLineResult());

        Assert.Equal(AndroidLicenseDetectionStatus.Cancelled, result.Status);
        Assert.Contains("No acceptance input", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DetectAsync_PreCancelled_DoesNotStartSdkManager()
    {
        using var fixture = new LicenseFixture(withSdkManager: true);
        var runner = new StubProcessRunner(Result(ProcessExecutionStatus.Succeeded, 0));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await new AndroidLicenseDetector(runner).DetectAsync(
            fixture.CommandLineResult(),
            cancellation.Token);

        Assert.Equal(AndroidLicenseDetectionStatus.Cancelled, result.Status);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task DetectAsync_MissingEffectiveSdkManager_ReturnsUnavailableWithoutProcess()
    {
        using var fixture = new LicenseFixture(withSdkManager: false);
        var runner = new StubProcessRunner(Result(ProcessExecutionStatus.Succeeded, 0));

        var result = await new AndroidLicenseDetector(runner).DetectAsync(fixture.CommandLineResult());

        Assert.Equal(AndroidLicenseDetectionStatus.SdkManagerUnavailable, result.Status);
        Assert.Empty(runner.Requests);
    }

    private static void AssertSafeProbe(ProcessRequest request, string sdkManagerPath)
    {
        Assert.Equal("cmd.exe", request.FileName, ignoreCase: true);
        Assert.Equal(TimeSpan.FromSeconds(10), request.Timeout);
        Assert.Equal("Read Android license status", request.DisplayName);
        Assert.Equal(5, request.Arguments.Count);
        Assert.Equal("/d", request.Arguments[0], ignoreCase: true);
        Assert.Equal("/v:off", request.Arguments[1], ignoreCase: true);
        Assert.Equal("/s", request.Arguments[2], ignoreCase: true);
        Assert.Equal("/c", request.Arguments[3], ignoreCase: true);
        var command = request.Arguments[4];
        Assert.Contains($"call \"{sdkManagerPath}\" --licenses", command, StringComparison.Ordinal);
        Assert.Contains("< NUL", command, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("echo y", command, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("yes |", command, StringComparison.OrdinalIgnoreCase);
    }

    private static ProcessResult Result(
        ProcessExecutionStatus status,
        int? exitCode,
        params ProcessOutputLine[] output)
    {
        var now = DateTimeOffset.UtcNow;
        return new ProcessResult(
            status,
            exitCode,
            now,
            now,
            output,
            "sdkmanager --licenses",
            status == ProcessExecutionStatus.Succeeded ? null : "probe status");
    }

    private static ProcessOutputLine Line(ProcessStream stream, string text)
        => new(DateTimeOffset.UtcNow, stream, text);

    private sealed class StubProcessRunner : IProcessRunner
    {
        private readonly ProcessResult _result;

        public StubProcessRunner(ProcessResult result)
        {
            _result = result;
        }

        public List<ProcessRequest> Requests { get; } = new();

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            foreach (var line in _result.Output)
                progress?.Report(line);
            return Task.FromResult(_result);
        }
    }

    private sealed class LicenseFixture : IDisposable
    {
        public LicenseFixture(bool withSdkManager)
        {
            SdkRoot = Path.Combine(Path.GetTempPath(), "FlutterBuildDoctorTests", "Licenses", Guid.NewGuid().ToString("N"));
            InstallationPath = Path.Combine(SdkRoot, "cmdline-tools", "latest");
            var bin = Path.Combine(InstallationPath, "bin");
            Directory.CreateDirectory(bin);
            SdkManagerPath = Path.Combine(bin, "sdkmanager.bat");
            if (withSdkManager)
                File.WriteAllText(SdkManagerPath, "@echo off");
        }

        public string SdkRoot { get; }
        public string InstallationPath { get; }
        public string? SdkManagerPath { get; }

        public void CreateLicense(string fileName)
        {
            var directory = Path.Combine(SdkRoot, "licenses");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, fileName), "hash");
        }

        public AndroidCommandLineToolsDetectionResult CommandLineResult()
        {
            var exists = File.Exists(SdkManagerPath);
            var candidate = new AndroidCommandLineToolsCandidate(
                InstallationPath,
                SdkManagerPath,
                "19.0",
                AndroidCommandLineToolsLayout.LatestAlias,
                IsEffective: true,
                SdkManagerExists: exists,
                SourcePropertiesPath: null,
                RawSourceProperties: null,
                Message: exists ? null : "sdkmanager missing");
            return new AndroidCommandLineToolsDetectionResult(
                exists
                    ? AndroidCommandLineToolsDetectionStatus.Succeeded
                    : AndroidCommandLineToolsDetectionStatus.EffectiveSdkManagerMissing,
                SdkRoot,
                candidate,
                new[] { candidate },
                HasMultipleInstallations: false,
                Message: exists ? "ready" : "missing");
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(SdkRoot))
                    Directory.Delete(SdkRoot, recursive: true);
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }
}
