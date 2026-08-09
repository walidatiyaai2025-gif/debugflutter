using FlutterBuildDoctor.App.DependencyInjection;
using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Infrastructure.Environment;
using FlutterBuildDoctor.Infrastructure.Processes;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Environment;

public sealed class GitToolDetectorTests
{
    [Fact]
    public async Task DetectAsync_returns_path_and_version_when_git_is_available()
    {
        const string gitPath = @"C:\Program Files\Git\cmd\git.exe";
        var runner = new QueueProcessRunner(
            Success(gitPath),
            Success("git version 2.51.0.windows.1"));
        var detector = new GitToolDetector(runner);

        var result = await detector.DetectAsync();

        Assert.True(result.Installed);
        Assert.Equal(gitPath, result.Path);
        Assert.Equal("2.51.0.windows.1", result.Version);
        Assert.Null(result.Message);
    }

    [Fact]
    public async Task DetectAsync_returns_missing_when_git_is_not_on_path()
    {
        var runner = new QueueProcessRunner(Failure());
        var detector = new GitToolDetector(runner);

        var result = await detector.DetectAsync();

        Assert.False(result.Installed);
        Assert.Null(result.Path);
        Assert.Null(result.Version);
        Assert.Equal("Git was not found on PATH.", result.Message);
    }

    [Fact]
    public async Task DetectAsync_finds_git_with_the_real_process_runner_on_windows()
    {
        Assert.True(OperatingSystem.IsWindows());
        var detector = new GitToolDetector(new ProcessRunner());

        var result = await detector.DetectAsync();

        Assert.True(result.Installed, result.Message);
        Assert.False(string.IsNullOrWhiteSpace(result.Path));
        Assert.EndsWith("git.exe", result.Path!, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(result.Version));
    }

    [Fact]
    public void Runtime_detection_registration_exposes_git_detector_and_scanner()
    {
        var services = new ServiceCollection();
        services.AddFlutterBuildDoctorRuntimeDetection();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<ProcessRunner>(provider.GetRequiredService<IProcessRunner>());
        Assert.Contains(provider.GetServices<IToolDetector>(), detector => detector is GitToolDetector);
        Assert.NotNull(provider.GetRequiredService<FlutterBuildDoctor.Application.Services.IEnvironmentScanner>());
    }

    private static ProcessResult Success(string line)
        => Result(ProcessExecutionStatus.Succeeded, 0, line);

    private static ProcessResult Failure()
        => Result(ProcessExecutionStatus.Failed, 1);

    private static ProcessResult Result(ProcessExecutionStatus status, int? exitCode, params string[] lines)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var output = lines
            .Select(line => new ProcessOutputLine(timestamp, ProcessStream.StdOut, line))
            .ToArray();

        return new ProcessResult(
            status,
            exitCode,
            timestamp,
            timestamp,
            output,
            "test-command",
            status == ProcessExecutionStatus.Succeeded ? null : "failed");
    }

    private sealed class QueueProcessRunner : IProcessRunner
    {
        private readonly Queue<ProcessResult> _results;

        public QueueProcessRunner(params ProcessResult[] results)
        {
            _results = new Queue<ProcessResult>(results);
        }

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_results.Dequeue());
        }
    }
}
