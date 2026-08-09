using FlutterBuildDoctor.Android.Detection;
using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.UnitTests.Android;

public sealed class AndroidAdbDetectorTests
{
    [Fact]
    public async Task DetectAsync_ValidAdb_ParsesProtocolPlatformVersionAndInstalledPath()
    {
        using var fixture = new AdbFixture(withPlatformTools: true, withAdb: true, platformRevision: "36.0.0");
        var runner = new StubProcessRunner(Result(
            ProcessExecutionStatus.Succeeded,
            0,
            Line(ProcessStream.StdOut, "Android Debug Bridge version 1.0.41"),
            Line(ProcessStream.StdOut, "Version 36.0.0-13206524"),
            Line(ProcessStream.StdOut, $"Installed as {fixture.AdbPath}")));
        var detector = new AndroidAdbDetector(runner);

        var result = await detector.DetectAsync(ValidRootResult(fixture.SdkRoot));

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal("1.0.41", result.AdbProtocolVersion);
        Assert.Equal("36.0.0-13206524", result.PlatformToolsVersion);
        Assert.Equal(fixture.AdbPath, result.AdbPath, ignoreCase: true);
        Assert.Equal(fixture.AdbPath, result.InstalledAsPath, ignoreCase: true);
        Assert.Contains("Android Debug Bridge version 1.0.41", result.RawVersionOutput, StringComparison.Ordinal);
        var request = Assert.Single(runner.Requests);
        Assert.Equal(fixture.AdbPath, request.FileName, ignoreCase: true);
        Assert.Equal(new[] { "version" }, request.Arguments);
        Assert.Equal(TimeSpan.FromSeconds(10), request.Timeout);
        Assert.Equal("Read ADB version", request.DisplayName);
    }

    [Fact]
    public async Task DetectAsync_CommandOmitsPackageVersion_FallsBackToSourcePropertiesRevision()
    {
        using var fixture = new AdbFixture(withPlatformTools: true, withAdb: true, platformRevision: "35.0.2");
        var runner = new StubProcessRunner(Result(
            ProcessExecutionStatus.Succeeded,
            0,
            Line(ProcessStream.StdOut, "Android Debug Bridge version 1.0.41")));

        var result = await new AndroidAdbDetector(runner).DetectAsync(ValidRootResult(fixture.SdkRoot));

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal("1.0.41", result.AdbProtocolVersion);
        Assert.Equal("35.0.2", result.PlatformToolsVersion);
        Assert.Contains("Pkg.Revision=35.0.2", result.RawSourceProperties, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetectAsync_MissingPlatformTools_ReturnsMissingWithoutRunningAdb()
    {
        using var fixture = new AdbFixture(withPlatformTools: false, withAdb: false, platformRevision: null);
        var runner = new StubProcessRunner(Result(ProcessExecutionStatus.Succeeded, 0));

        var result = await new AndroidAdbDetector(runner).DetectAsync(ValidRootResult(fixture.SdkRoot));

        Assert.Equal(AndroidAdbDetectionStatus.PlatformToolsMissing, result.Status);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task DetectAsync_PlatformToolsWithoutAdb_ReturnsAdbMissingAndPreservesPackageRevision()
    {
        using var fixture = new AdbFixture(withPlatformTools: true, withAdb: false, platformRevision: "34.0.5");
        var runner = new StubProcessRunner(Result(ProcessExecutionStatus.Succeeded, 0));

        var result = await new AndroidAdbDetector(runner).DetectAsync(ValidRootResult(fixture.SdkRoot));

        Assert.Equal(AndroidAdbDetectionStatus.AdbMissing, result.Status);
        Assert.Equal("34.0.5", result.PlatformToolsVersion);
        Assert.Null(result.AdbPath);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task DetectAsync_ProbeTimeout_ReturnsTimedOutWithRawEvidence()
    {
        using var fixture = new AdbFixture(withPlatformTools: true, withAdb: true, platformRevision: "36.0.0");
        var runner = new StubProcessRunner(Result(
            ProcessExecutionStatus.TimedOut,
            null,
            Line(ProcessStream.StdErr, "partial adb output")));

        var result = await new AndroidAdbDetector(runner).DetectAsync(ValidRootResult(fixture.SdkRoot));

        Assert.Equal(AndroidAdbDetectionStatus.TimedOut, result.Status);
        Assert.Equal("36.0.0", result.PlatformToolsVersion);
        Assert.Contains("partial adb output", result.RawVersionOutput, StringComparison.Ordinal);
        Assert.NotNull(result.ProbeResult);
    }

    [Fact]
    public async Task DetectAsync_ProbeFailure_ReturnsProbeFailedWithoutClaimingVersion()
    {
        using var fixture = new AdbFixture(withPlatformTools: true, withAdb: true, platformRevision: "36.0.0");
        var runner = new StubProcessRunner(Result(
            ProcessExecutionStatus.Failed,
            1,
            Line(ProcessStream.StdErr, "adb failed")));

        var result = await new AndroidAdbDetector(runner).DetectAsync(ValidRootResult(fixture.SdkRoot));

        Assert.Equal(AndroidAdbDetectionStatus.ProbeFailed, result.Status);
        Assert.Null(result.AdbProtocolVersion);
        Assert.Equal("36.0.0", result.PlatformToolsVersion);
        Assert.Contains("adb failed", result.RawVersionOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetectAsync_UnparseableSuccess_PreservesRawOutputAndReturnsParseFailed()
    {
        using var fixture = new AdbFixture(withPlatformTools: true, withAdb: true, platformRevision: null);
        var runner = new StubProcessRunner(Result(
            ProcessExecutionStatus.Succeeded,
            0,
            Line(ProcessStream.StdOut, "unexpected adb banner")));

        var result = await new AndroidAdbDetector(runner).DetectAsync(ValidRootResult(fixture.SdkRoot));

        Assert.Equal(AndroidAdbDetectionStatus.ParseFailed, result.Status);
        Assert.Contains("unexpected adb banner", result.RawVersionOutput, StringComparison.Ordinal);
        Assert.Null(result.AdbProtocolVersion);
        Assert.Null(result.PlatformToolsVersion);
    }

    [Fact]
    public async Task DetectAsync_PreCancelled_ReturnsCancelledWithoutRunningAdb()
    {
        using var fixture = new AdbFixture(withPlatformTools: true, withAdb: true, platformRevision: "36.0.0");
        var runner = new StubProcessRunner(Result(ProcessExecutionStatus.Succeeded, 0));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await new AndroidAdbDetector(runner).DetectAsync(
            ValidRootResult(fixture.SdkRoot),
            cancellation.Token);

        Assert.Equal(AndroidAdbDetectionStatus.Cancelled, result.Status);
        Assert.Empty(runner.Requests);
    }

    private static AndroidSdkRootDetectionResult ValidRootResult(string sdkRoot)
    {
        var platformToolsPath = Path.Combine(sdkRoot, "platform-tools");
        var candidate = new AndroidSdkRootCandidate(
            Path.GetFullPath(sdkRoot),
            Array.Empty<AndroidSdkRootSourceEvidence>(),
            IsEffective: true,
            Exists: true,
            HasRecognizedSdkLayout: true,
            HasPlatformToolsDirectory: Directory.Exists(platformToolsPath),
            HasPlatformsDirectory: false,
            HasBuildToolsDirectory: false,
            HasCmdlineToolsDirectory: false,
            HasLicensesDirectory: false,
            ValidationMessage: null);
        return new AndroidSdkRootDetectionResult(
            AndroidSdkRootDetectionStatus.Succeeded,
            candidate,
            new[] { candidate },
            HasConflict: false,
            Message: "valid");
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
            "adb version",
            status == ProcessExecutionStatus.Succeeded ? null : "probe failure");
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

    private sealed class AdbFixture : IDisposable
    {
        public AdbFixture(bool withPlatformTools, bool withAdb, string? platformRevision)
        {
            SdkRoot = Path.Combine(Path.GetTempPath(), "FlutterBuildDoctorTests", "Adb", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(SdkRoot);
            if (!withPlatformTools)
                return;

            PlatformToolsPath = Path.Combine(SdkRoot, "platform-tools");
            Directory.CreateDirectory(PlatformToolsPath);
            if (platformRevision is not null)
                File.WriteAllText(Path.Combine(PlatformToolsPath, "source.properties"), $"Pkg.Revision={platformRevision}\n");
            if (withAdb)
            {
                AdbPath = Path.Combine(PlatformToolsPath, "adb.exe");
                File.WriteAllText(AdbPath, "fixture");
            }
        }

        public string SdkRoot { get; }
        public string? PlatformToolsPath { get; }
        public string? AdbPath { get; }

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
