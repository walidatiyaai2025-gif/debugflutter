using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Git.Branches;

namespace FlutterBuildDoctor.UnitTests.Git;

public sealed class GitBranchServiceTests
{
    [Fact]
    public async Task GetBranchesAsync_refreshes_and_returns_local_and_remote_branches()
    {
        using var repository = new TempRepository();
        var progressLine = new ProcessOutputLine(
            DateTimeOffset.UtcNow,
            ProcessStream.StdErr,
            "Fetching origin");
        var runner = new QueueProcessRunner(
            (_, progress) =>
            {
                progress?.Report(progressLine);
                return Result(ProcessExecutionStatus.Succeeded, 0);
            },
            (_, _) => Result(
                ProcessExecutionStatus.Succeeded,
                0,
                "refs/heads/feature/ui\t2222222\t\t \t",
                "refs/heads/main\t1111111\torigin/main\t*\t",
                "refs/remotes/origin/HEAD\t1111111\t\t \trefs/remotes/origin/main",
                "refs/remotes/origin/main\t1111111\t\t \t",
                "refs/remotes/upstream/feature/ui\t3333333\t\t \t"));
        var service = new GitBranchService(runner);
        var progress = new CapturingProgress();

        var result = await service.GetBranchesAsync(
            new GitBranchDiscoveryRequest("git.exe", repository.Path),
            progress);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(GitBranchDiscoveryStatus.Succeeded, result.Status);
        Assert.Equal(4, result.Branches.Count);
        Assert.Equal(progressLine, Assert.Single(progress.Lines));

        var current = Assert.Single(result.Branches.Where(branch => branch.IsCurrent));
        Assert.Equal("main", current.Name);
        Assert.Equal(GitBranchKind.Local, current.Kind);
        Assert.Equal("origin/main", current.Upstream);

        var originMain = Assert.Single(result.Branches.Where(branch =>
            branch.Kind == GitBranchKind.Remote &&
            branch.RemoteName == "origin" &&
            branch.Name == "main"));
        Assert.Equal("refs/remotes/origin/main", originMain.FullName);

        var upstreamFeature = Assert.Single(result.Branches.Where(branch =>
            branch.Kind == GitBranchKind.Remote &&
            branch.RemoteName == "upstream"));
        Assert.Equal("feature/ui", upstreamFeature.Name);

        Assert.DoesNotContain(result.Branches, branch => branch.FullName.EndsWith("/HEAD", StringComparison.Ordinal));

        Assert.Equal(
            new[] { "fetch", "--prune", "--all", "--no-tags" },
            runner.Requests[0].Arguments);
        Assert.Equal("for-each-ref", runner.Requests[1].Arguments[0]);
        Assert.Equal(repository.Path, runner.Requests[0].WorkingDirectory);
        Assert.Equal("0", runner.Requests[0].Environment!["GIT_TERMINAL_PROMPT"]);
        Assert.Equal("Never", runner.Requests[0].Environment["GCM_INTERACTIVE"]);
    }

    [Fact]
    public async Task GetBranchesAsync_returns_cached_refs_when_remote_refresh_fails()
    {
        using var repository = new TempRepository();
        var runner = new QueueProcessRunner(
            (_, _) => Result(ProcessExecutionStatus.Failed, 1, failureReason: "network unavailable"),
            (_, _) => Result(
                ProcessExecutionStatus.Succeeded,
                0,
                "refs/heads/main\t1111111\t\t*\t"));
        var service = new GitBranchService(runner);

        var result = await service.GetBranchesAsync(
            new GitBranchDiscoveryRequest("git.exe", repository.Path));

        Assert.True(result.IsSuccess);
        Assert.Equal(GitBranchDiscoveryStatus.SucceededWithWarning, result.Status);
        Assert.Single(result.Branches);
        Assert.Contains("network unavailable", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.RefreshResult);
        Assert.NotNull(result.ListResult);
        Assert.Equal(2, runner.Requests.Count);
    }

    [Fact]
    public async Task GetBranchesAsync_stops_when_remote_refresh_times_out()
    {
        using var repository = new TempRepository();
        var runner = new QueueProcessRunner(
            (_, _) => Result(ProcessExecutionStatus.TimedOut, null, failureReason: "Process timed out."));
        var service = new GitBranchService(runner);

        var result = await service.GetBranchesAsync(
            new GitBranchDiscoveryRequest(
                "git.exe",
                repository.Path,
                Timeout: TimeSpan.FromSeconds(3)));

        Assert.Equal(GitBranchDiscoveryStatus.TimedOut, result.Status);
        Assert.Empty(result.Branches);
        Assert.Single(runner.Requests);
        Assert.Equal(TimeSpan.FromSeconds(3), runner.Requests[0].Timeout);
    }

    [Fact]
    public async Task GetBranchesAsync_can_list_cached_refs_without_refreshing_remotes()
    {
        using var repository = new TempRepository();
        var runner = new QueueProcessRunner(
            (_, _) => Result(
                ProcessExecutionStatus.Succeeded,
                0,
                "refs/heads/main\t1111111\t\t*\t",
                "refs/remotes/origin/dev\t2222222\t\t \t"));
        var service = new GitBranchService(runner);

        var result = await service.GetBranchesAsync(
            new GitBranchDiscoveryRequest(
                "git.exe",
                repository.Path,
                RefreshRemotes: false));

        Assert.Equal(GitBranchDiscoveryStatus.Succeeded, result.Status);
        Assert.Equal(2, result.Branches.Count);
        Assert.Single(runner.Requests);
        Assert.Equal("for-each-ref", runner.Requests[0].Arguments[0]);
        Assert.Null(result.RefreshResult);
    }

    [Fact]
    public async Task GetBranchesAsync_rejects_non_git_folder_before_running_git()
    {
        using var directory = new TempDirectory(createGitMetadata: false);
        var runner = new QueueProcessRunner(
            (_, _) => throw new InvalidOperationException("Git must not run."));
        var service = new GitBranchService(runner);

        var result = await service.GetBranchesAsync(
            new GitBranchDiscoveryRequest("git.exe", directory.Path));

        Assert.Equal(GitBranchDiscoveryStatus.InvalidRepository, result.Status);
        Assert.Empty(result.Branches);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task GetBranchesAsync_maps_branch_list_failure()
    {
        using var repository = new TempRepository();
        var runner = new QueueProcessRunner(
            (_, _) => Result(ProcessExecutionStatus.Succeeded, 0),
            (_, _) => Result(ProcessExecutionStatus.Failed, 128, failureReason: "bad ref database"));
        var service = new GitBranchService(runner);

        var result = await service.GetBranchesAsync(
            new GitBranchDiscoveryRequest("git.exe", repository.Path));

        Assert.Equal(GitBranchDiscoveryStatus.Failed, result.Status);
        Assert.Empty(result.Branches);
        Assert.Contains("bad ref database", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, runner.Requests.Count);
    }

    private static ProcessResult Result(
        ProcessExecutionStatus status,
        int? exitCode,
        params string[] lines)
        => Result(status, exitCode, null, lines);

    private static ProcessResult Result(
        ProcessExecutionStatus status,
        int? exitCode,
        string? failureReason,
        params string[] lines)
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
            "git test",
            failureReason);
    }

    private sealed class QueueProcessRunner : IProcessRunner
    {
        private readonly Queue<Func<ProcessRequest, IProgress<ProcessOutputLine>?, ProcessResult>> _handlers;

        public QueueProcessRunner(
            params Func<ProcessRequest, IProgress<ProcessOutputLine>?, ProcessResult>[] handlers)
        {
            _handlers = new Queue<Func<ProcessRequest, IProgress<ProcessOutputLine>?, ProcessResult>>(handlers);
        }

        public List<ProcessRequest> Requests { get; } = new();

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(_handlers.Dequeue()(request, progress));
        }
    }

    private sealed class CapturingProgress : IProgress<ProcessOutputLine>
    {
        public List<ProcessOutputLine> Lines { get; } = new();

        public void Report(ProcessOutputLine value) => Lines.Add(value);
    }

    private sealed class TempRepository : TempDirectory
    {
        public TempRepository() : base(createGitMetadata: true)
        {
        }
    }

    private class TempDirectory : IDisposable
    {
        public TempDirectory(bool createGitMetadata)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "FlutterBuildDoctor.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
            if (createGitMetadata)
            {
                Directory.CreateDirectory(System.IO.Path.Combine(Path, ".git"));
            }
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup for test temp data.
            }
        }
    }
}
