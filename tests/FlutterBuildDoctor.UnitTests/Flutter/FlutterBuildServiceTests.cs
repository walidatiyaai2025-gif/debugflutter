using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Flutter.Build;
using FlutterBuildDoctor.Flutter.Commands;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class FlutterBuildServiceTests
{
    [Fact]
    public async Task BuildAsync_ReturnsArtifactHashAndExecutionReceipt()
    {
        var root = CreateBuildRoot(out var artifactPath);
        try
        {
            var runner = new SequencedRunner(Success());
            var service = CreateService(runner);
            var request = Request(root);

            var receipt = await service.BuildAsync(request);

            Assert.True(receipt.IsSuccess);
            Assert.Equal(1, receipt.AttemptCount);
            Assert.NotNull(receipt.Artifact);
            Assert.Equal(Path.GetFullPath(artifactPath), receipt.Artifact!.Path);
            Assert.Equal(64, receipt.Artifact.Sha256!.Length);
            Assert.True(receipt.Duration >= TimeSpan.Zero);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildAsync_RetriesOneTransientFailureThenSucceeds()
    {
        var root = CreateBuildRoot(out _);
        try
        {
            var runner = new SequencedRunner(
                Failed("Gradle daemon disappeared unexpectedly."),
                Success());
            var service = CreateService(runner);

            var receipt = await service.BuildAsync(Request(root));

            Assert.True(receipt.IsSuccess);
            Assert.Equal(2, receipt.AttemptCount);
            Assert.Contains("daemon disappeared", receipt.Attempts[0].RetryReason!, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(2, runner.CallCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildAsync_DoesNotRetryNormalCompileFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), $"fbd-build-service-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var runner = new SequencedRunner(Failed("Compilation failed: undefined name."));
            var service = CreateService(runner);

            var receipt = await service.BuildAsync(Request(root));

            Assert.Equal(FlutterBuildStatus.Failed, receipt.Status);
            Assert.Equal(1, receipt.AttemptCount);
            Assert.Equal(1, runner.CallCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildAsync_ReportsArtifactMissingWhenFlutterClaimsSuccessWithoutOutput()
    {
        var root = Path.Combine(Path.GetTempPath(), $"fbd-build-service-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var service = CreateService(new SequencedRunner(Success()));

            var receipt = await service.BuildAsync(Request(root));

            Assert.Equal(FlutterBuildStatus.ArtifactMissing, receipt.Status);
            Assert.False(receipt.IsSuccess);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static FlutterBuildService CreateService(IProcessRunner runner)
        => new(
            runner,
            new FlutterBuildRequestBuilder(),
            new BuildArtifactLocator(),
            new Sha256ArtifactHashService(),
            new BuildRetryPolicy(maxRetries: 1));

    private static FlutterBuildRequest Request(string root)
        => new(
            new FlutterCommandContext("flutter", root),
            FlutterBuildArtifactType.Apk,
            FlutterBuildMode.Release);

    private static string CreateBuildRoot(out string artifactPath)
    {
        var root = Path.Combine(Path.GetTempPath(), $"fbd-build-service-{Guid.NewGuid():N}");
        artifactPath = Path.Combine(root, "build", "app", "outputs", "flutter-apk", "app-release.apk");
        Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
        File.WriteAllText(artifactPath, "verified-apk");
        return root;
    }

    private static ProcessResult Success()
        => Result(ProcessExecutionStatus.Succeeded, 0, null, "Built build/app/outputs/flutter-apk/app-release.apk");

    private static ProcessResult Failed(string reason)
        => Result(ProcessExecutionStatus.Failed, 1, reason, reason);

    private static ProcessResult Result(
        ProcessExecutionStatus status,
        int? exitCode,
        string? failureReason,
        params string[] output)
    {
        var now = DateTimeOffset.UtcNow;
        return new ProcessResult(
            status,
            exitCode,
            now,
            now.AddMilliseconds(50),
            output.Select(line => new ProcessOutputLine(now, ProcessStream.StdErr, line)).ToArray(),
            "flutter build apk --release",
            failureReason);
    }

    private sealed class SequencedRunner : IProcessRunner
    {
        private readonly Queue<ProcessResult> _results;

        public SequencedRunner(params ProcessResult[] results)
            => _results = new Queue<ProcessResult>(results);

        public int CallCount { get; private set; }

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_results.Dequeue());
        }
    }
}
