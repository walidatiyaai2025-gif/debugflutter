using FlutterBuildDoctor.Android.Detection;
using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.UnitTests.Android;

public sealed class AndroidEmulatorDetectorTests
{
    [Fact]
    public async Task DetectAsync_ValidEmulator_ParsesVersionAndBuildsReadOnlyProbe()
    {
        using var fixture = new EmulatorFixture(withDirectory: true, withExecutable: true, revision: "36.1.9.0");
        var runner = new StubProcessRunner(Result(
            ProcessExecutionStatus.Succeeded,
            0,
            Line(ProcessStream.StdOut, "Android emulator version 36.1.9.0 (build_id 14000000)")));

        var result = await new AndroidEmulatorDetector(runner).DetectAsync(ValidRootResult(fixture.SdkRoot));

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal("36.1.9.0", result.Version);
        Assert.Equal(AndroidEmulatorVersionSource.CommandOutput, result.VersionSource);
        Assert.Equal(fixture.EmulatorPath, result.EmulatorPath, ignoreCase: true);
        var request = Assert.Single(runner.Requests);
        Assert.Equal(fixture.EmulatorPath, request.FileName, ignoreCase: true);
        Assert.Equal(new[] { "-version" }, request.Arguments);
        Assert.Equal(TimeSpan.FromSeconds(10), request.Timeout);
        Assert.Equal("Read Android emulator version", request.DisplayName);
    }

    [Fact]
    public async Task DetectAsync_UnparseableSuccessfulOutput_FallsBackToPackageRevision()
    {
        using var fixture = new EmulatorFixture(withDirectory: true, withExecutable: true, revision: "35.6.11.0");
        var runner = new StubProcessRunner(Result(
            ProcessExecutionStatus.Succeeded,
            0,
            Line(ProcessStream.StdOut, "unexpected emulator banner")));

        var result = await new AndroidEmulatorDetector(runner).DetectAsync(ValidRootResult(fixture.SdkRoot));

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal("35.6.11.0", result.Version);
        Assert.Equal(AndroidEmulatorVersionSource.SourceProperties, result.VersionSource);
        Assert.Contains("unexpected emulator banner", result.RawVersionOutput, StringComparison.Ordinal);
        Assert.Contains("Pkg.Revision=35.6.11.0", result.RawSourceProperties, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetectAsync_CommandAndPackageVersionDiffer_PreservesMismatchEvidence()
    {
        using var fixture = new EmulatorFixture(withDirectory: true, withExecutable: true, revision: "36.1.8.0");
        var runner = new StubProcessRunner(Result(
            ProcessExecutionStatus.Succeeded,
            0,
            Line(ProcessStream.StdErr, "Android emulator version 36.1.9.0 (build_id 14000000)")));

        var result = await new AndroidEmulatorDetector(runner).DetectAsync(ValidRootResult(fixture.SdkRoot));

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal("36.1.9.0", result.Version);
        Assert.Equal(AndroidEmulatorVersionSource.CommandOutput, result.VersionSource);
        Assert.Contains("Package metadata reports revision 36.1.8.0", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DetectAsync_MissingDirectory_ReturnsMissingWithoutProcess()
    {
        using var fixture = new EmulatorFixture(withDirectory: false, withExecutable: false, revision: null);
        var runner = new StubProcessRunner(Result(ProcessExecutionStatus.Succeeded, 0));

        var result = await new AndroidEmulatorDetector(runner).DetectAsync(ValidRootResult(fixture.SdkRoot));

        Assert.Equal(AndroidEmulatorDetectionStatus.EmulatorDirectoryMissing, result.Status);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task DetectAsync_PackageWithoutExecutable_ReturnsMissingAndKeepsPackageRevision()
    {
        using var fixture = new EmulatorFixture(withDirectory: true, withExecutable: false, revision: "36.1.9.0");
        var runner = new StubProcessRunner(Result(ProcessExecutionStatus.Succeeded, 0));

        var result = await new AndroidEmulatorDetector(runner).DetectAsync(ValidRootResult(fixture.SdkRoot));

        Assert.Equal(AndroidEmulatorDetectionStatus.EmulatorMissing, result.Status);
        Assert.Equal("36.1.9.0", result.Version);
        Assert.Equal(AndroidEmulatorVersionSource.SourceProperties, result.VersionSource);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task DetectAsync_Timeout_ReturnsTimedOutWithMetadataEvidence()
    {
        using var fixture = new EmulatorFixture(withDirectory: true, withExecutable: true, revision: "36.1.9.0");
        var runner = new StubProcessRunner(Result(
            ProcessExecutionStatus.TimedOut,
            null,
            Line(ProcessStream.StdErr, "partial output")));

        var result = await new AndroidEmulatorDetector(runner).DetectAsync(ValidRootResult(fixture.SdkRoot));

        Assert.Equal(AndroidEmulatorDetectionStatus.TimedOut, result.Status);
        Assert.Equal("36.1.9.0", result.Version);
        Assert.Contains("partial output", result.RawVersionOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetectAsync_ProbeFailure_ReturnsFailureWithoutClaimingCommandVersion()
    {
        using var fixture = new EmulatorFixture(withDirectory: true, withExecutable: true, revision: "36.1.9.0");
        var runner = new StubProcessRunner(Result(
            ProcessExecutionStatus.Failed,
            1,
            Line(ProcessStream.StdErr, "probe failed")));

        var result = await new AndroidEmulatorDetector(runner).DetectAsync(ValidRootResult(fixture.SdkRoot));

        Assert.Equal(AndroidEmulatorDetectionStatus.ProbeFailed, result.Status);
        Assert.Equal("36.1.9.0", result.Version);
        Assert.Equal(AndroidEmulatorVersionSource.SourceProperties, result.VersionSource);
        Assert.Contains("probe failed", result.RawVersionOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetectAsync_NoParseableVersionAndNoMetadata_ReturnsVersionUnavailable()
    {
        using var fixture = new EmulatorFixture(withDirectory: true, withExecutable: true, revision: null);
        var runner = new StubProcessRunner(Result(
            ProcessExecutionStatus.Succeeded,
            0,
            Line(ProcessStream.StdOut, "unknown output")));

        var result = await new AndroidEmulatorDetector(runner).DetectAsync(ValidRootResult(fixture.SdkRoot));

        Assert.Equal(AndroidEmulatorDetectionStatus.VersionUnavailable, result.Status);
        Assert.Null(result.Version);
        Assert.Equal(AndroidEmulatorVersionSource.None, result.VersionSource);
        Assert.Contains("unknown output", result.RawVersionOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetectAsync_PreCancelled_ReturnsCancelledWithoutProcess()
    {
        using var fixture = new EmulatorFixture(withDirectory: true, withExecutable: true, revision: "36.1.9.0");
        var runner = new StubProcessRunner(Result(ProcessExecutionStatus.Succeeded, 0));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await new AndroidEmulatorDetector(runner).DetectAsync(
            ValidRootResult(fixture.SdkRoot),
            cancellation.Token);

        Assert.Equal(AndroidEmulatorDetectionStatus.Cancelled, result.Status);
        Assert.Empty(runner.Requests);
    }

    private static AndroidSdkRootDetectionResult ValidRootResult(string sdkRoot)
    {
        var candidate = new AndroidSdkRootCandidate(
            Path.GetFullPath(sdkRoot),
            Array.Empty<AndroidSdkRootSourceEvidence>(),
            IsEffective: true,
            Exists: true,
            HasRecognizedSdkLayout: true,
            HasPlatformToolsDirectory: false,
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
            "emulator -version",
            status == ProcessExecutionStatus.Succeeded ? null : "probe failure");
    }

    private static ProcessOutputLine Line(ProcessStream stream, string text)
        => new(DateTimeOffset.UtcNow, stream, text);

    private sealed class StubProcessRunner : IProcessRunner
    {
        private readonly ProcessResult _result;

        public StubProcessRunner(ProcessResult result) => _result = result;

        public List<ProcessRequest> Requests { get; } = new();

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(_result);
        }
    }

    private sealed class EmulatorFixture : IDisposable
    {
        public EmulatorFixture(bool withDirectory, bool withExecutable, string? revision)
        {
            SdkRoot = Path.Combine(Path.GetTempPath(), "FlutterBuildDoctorTests", "Emulator", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(SdkRoot);
            if (!withDirectory)
                return;

            EmulatorDirectory = Path.Combine(SdkRoot, "emulator");
            Directory.CreateDirectory(EmulatorDirectory);
            if (revision is not null)
                File.WriteAllText(Path.Combine(EmulatorDirectory, "source.properties"), $"Pkg.Revision={revision}\n");
            if (withExecutable)
            {
                EmulatorPath = Path.Combine(EmulatorDirectory, "emulator.exe");
                File.WriteAllText(EmulatorPath, "fixture");
            }
        }

        public string SdkRoot { get; }
        public string? EmulatorDirectory { get; }
        public string? EmulatorPath { get; }

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
