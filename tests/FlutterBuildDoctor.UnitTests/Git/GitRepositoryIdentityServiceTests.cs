using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Git.Repository;

namespace FlutterBuildDoctor.UnitTests.Git;

public sealed class GitRepositoryIdentityServiceTests
{
    private const string CommitSha = "1111111111111111111111111111111111111111";

    [Fact]
    public async Task ReadAsync_returns_exact_branch_commit_upstream_and_remote_name()
    {
        using var repository = new TempRepository();
        var runner = new QueueProcessRunner(
            (_, _) => Success(StdOut("main")),
            (_, _) => Success(StdOut(CommitSha)),
            (_, _) => Success(StdOut("origin/main")),
            (_, _) => Success(StdOut("origin")));
        var service = new GitRepositoryIdentityService(runner);

        var result = await service.ReadAsync(
            new GitRepositoryIdentityRequest("git.exe", repository.Path));

        Assert.True(result.IsSuccess, result.Message);
        Assert.NotNull(result.Identity);
        Assert.Equal("main", result.Identity!.BranchName);
        Assert.Equal(CommitSha, result.Identity.CommitSha);
        Assert.Equal("111111111111", result.Identity.ShortCommitSha);
        Assert.Equal("origin/main", result.Identity.Upstream);
        Assert.Equal("origin", result.Identity.RemoteName);
        Assert.False(result.Identity.IsDetached);
        Assert.Equal(4, runner.Requests.Count);
        Assert.Equal(new[] { "branch", "--show-current" }, runner.Requests[0].Arguments);
        Assert.Equal(new[] { "rev-parse", "--verify", "HEAD" }, runner.Requests[1].Arguments);
        Assert.Equal(
            new[] { "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}" },
            runner.Requests[2].Arguments);
        Assert.Equal(new[] { "config", "--get", "branch.main.remote" }, runner.Requests[3].Arguments);
        Assert.DoesNotContain(
            runner.Requests,
            request => request.Arguments.SequenceEqual(new[] { "remote", "get-url", "origin" }));
    }

    [Fact]
    public async Task ReadAsync_reports_detached_head_without_remote_probes()
    {
        using var repository = new TempRepository();
        var runner = new QueueProcessRunner(
            (_, _) => Success(),
            (_, _) => Success(StdOut(CommitSha)));
        var service = new GitRepositoryIdentityService(runner);

        var result = await service.ReadAsync(
            new GitRepositoryIdentityRequest("git.exe", repository.Path));

        Assert.True(result.IsSuccess, result.Message);
        Assert.NotNull(result.Identity);
        Assert.True(result.Identity!.IsDetached);
        Assert.Null(result.Identity.BranchName);
        Assert.Null(result.Identity.Upstream);
        Assert.Null(result.Identity.RemoteName);
        Assert.Equal(2, runner.Requests.Count);
    }

    [Fact]
    public async Task ReadAsync_keeps_local_branch_identity_when_tracking_metadata_is_absent()
    {
        using var repository = new TempRepository();
        var runner = new QueueProcessRunner(
            (_, _) => Success(StdOut("work")),
            (_, _) => Success(StdOut(CommitSha)),
            (_, _) => Failure(ProcessExecutionStatus.Failed, 128, "no upstream"),
            (_, _) => Failure(ProcessExecutionStatus.Failed, 1, "no remote"));
        var service = new GitRepositoryIdentityService(runner);

        var result = await service.ReadAsync(
            new GitRepositoryIdentityRequest("git.exe", repository.Path));

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal("work", result.Identity!.BranchName);
        Assert.Equal(CommitSha, result.Identity.CommitSha);
        Assert.Null(result.Identity.Upstream);
        Assert.Null(result.Identity.RemoteName);
    }

    [Fact]
    public async Task ReadAsync_maps_commit_timeout_without_claiming_identity_success()
    {
        using var repository = new TempRepository();
        var runner = new QueueProcessRunner(
            (_, _) => Success(StdOut("main")),
            (_, _) => Failure(ProcessExecutionStatus.TimedOut, null, "Process timed out."));
        var service = new GitRepositoryIdentityService(runner);

        var result = await service.ReadAsync(
            new GitRepositoryIdentityRequest("git.exe", repository.Path));

        Assert.Equal(GitRepositoryIdentityStatus.TimedOut, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Identity);
        Assert.Equal(2, runner.Requests.Count);
    }

    [Fact]
    public async Task ReadAsync_rejects_non_git_folder_before_running_git()
    {
        using var directory = new TempDirectory(createGitMetadata: false);
        var runner = new QueueProcessRunner(
            (_, _) => throw new InvalidOperationException("Git must not run."));
        var service = new GitRepositoryIdentityService(runner);

        var result = await service.ReadAsync(
            new GitRepositoryIdentityRequest("git.exe", directory.Path));

        Assert.Equal(GitRepositoryIdentityStatus.InvalidRepository, result.Status);
        Assert.Empty(runner.Requests);
    }

    private static ProcessOutputLine StdOut(string text)
        => new(DateTimeOffset.UtcNow, ProcessStream.StdOut, text);

    private static ProcessResult Success(params ProcessOutputLine[] output)
        => CreateResult(ProcessExecutionStatus.Succeeded, 0, null, output);

    private static ProcessResult Failure(
        ProcessExecutionStatus status,
        int? exitCode,
        string failureReason)
        => CreateResult(status, exitCode, failureReason, Array.Empty<ProcessOutputLine>());

    private static ProcessResult CreateResult(
        ProcessExecutionStatus status,
        int? exitCode,
        string? failureReason,
        IReadOnlyList<ProcessOutputLine> output)
    {
        var timestamp = DateTimeOffset.UtcNow;
        return new ProcessResult(
            status,
            exitCode,
            timestamp,
            timestamp,
            output,
            "git identity test",
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
