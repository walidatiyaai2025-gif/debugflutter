using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Git.Branches;

namespace FlutterBuildDoctor.UnitTests.Git;

public sealed class GitPullServiceTests
{
    private const string BeforeSha = "1111111111111111111111111111111111111111";
    private const string AfterSha = "2222222222222222222222222222222222222222";

    [Fact]
    public async Task PullAsync_reports_up_to_date_and_uses_ff_only()
    {
        using var repository = new TempRepository();
        var progressLine = StdErr("Already up to date.");
        var runner = new QueueProcessRunner(
            (_, _) => Success(StdOut("main")),
            (_, _) => Success(StdOut("origin/main")),
            (_, _) => Success(StdOut(BeforeSha)),
            (_, progress) =>
            {
                progress?.Report(progressLine);
                return Success(progressLine);
            },
            (_, _) => Success(StdOut("main")),
            (_, _) => Success(StdOut(BeforeSha)));
        var service = new GitPullService(runner);
        var progress = new CapturingProgress();

        var result = await service.PullAsync(
            new GitPullRequest("git.exe", repository.Path),
            progress);

        Assert.True(result.IsSuccess, result.Message);
        Assert.False(result.Changed);
        Assert.Equal(GitPullStatus.UpToDate, result.Status);
        Assert.Equal("main", result.CurrentBranch);
        Assert.Equal("origin/main", result.Upstream);
        Assert.Equal(BeforeSha, result.BeforeCommitSha);
        Assert.Equal(BeforeSha, result.AfterCommitSha);
        Assert.Contains(progressLine, progress.Lines);
        Assert.Equal(new[] { "pull", "--ff-only" }, runner.Requests[3].Arguments);
        Assert.DoesNotContain("--rebase", runner.Requests[3].Arguments);
        Assert.DoesNotContain("--force", runner.Requests[3].Arguments);
        Assert.DoesNotContain("reset", runner.Requests[3].Arguments);
        Assert.Equal("0", runner.Requests[3].Environment!["GIT_TERMINAL_PROMPT"]);
        Assert.Equal("Never", runner.Requests[3].Environment!["GCM_INTERACTIVE"]);
    }

    [Fact]
    public async Task PullAsync_reports_fast_forward_when_head_changes()
    {
        using var repository = new TempRepository();
        var runner = SuccessfulRunner(BeforeSha, AfterSha);
        var service = new GitPullService(runner);

        var result = await service.PullAsync(
            new GitPullRequest("git.exe", repository.Path));

        Assert.True(result.IsSuccess, result.Message);
        Assert.True(result.Changed);
        Assert.Equal(GitPullStatus.FastForwarded, result.Status);
        Assert.Equal(BeforeSha, result.BeforeCommitSha);
        Assert.Equal(AfterSha, result.AfterCommitSha);
        Assert.Contains("Fast-forwarded", result.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PullAsync_rejects_detached_head_before_pull()
    {
        using var repository = new TempRepository();
        var runner = new QueueProcessRunner(
            (_, _) => Success());
        var service = new GitPullService(runner);

        var result = await service.PullAsync(
            new GitPullRequest("git.exe", repository.Path));

        Assert.Equal(GitPullStatus.DetachedHead, result.Status);
        Assert.Single(runner.Requests);
    }

    [Fact]
    public async Task PullAsync_reports_no_upstream_and_does_not_pull()
    {
        using var repository = new TempRepository();
        var runner = new QueueProcessRunner(
            (_, _) => Success(StdOut("main")),
            (_, _) => Failure(
                ProcessExecutionStatus.Failed,
                128,
                "no upstream configured",
                StdErr("fatal: no upstream configured for branch 'main'")));
        var service = new GitPullService(runner);

        var result = await service.PullAsync(
            new GitPullRequest("git.exe", repository.Path));

        Assert.Equal(GitPullStatus.NoUpstream, result.Status);
        Assert.Equal("main", result.CurrentBranch);
        Assert.Contains("no upstream", result.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, runner.Requests.Count);
    }

    [Fact]
    public async Task PullAsync_preserves_ff_only_failure_for_diverged_branch()
    {
        using var repository = new TempRepository();
        var runner = new QueueProcessRunner(
            (_, _) => Success(StdOut("main")),
            (_, _) => Success(StdOut("origin/main")),
            (_, _) => Success(StdOut(BeforeSha)),
            (_, _) => Failure(
                ProcessExecutionStatus.Failed,
                128,
                "Not possible to fast-forward",
                StdErr("fatal: Not possible to fast-forward, aborting.")));
        var service = new GitPullService(runner);

        var result = await service.PullAsync(
            new GitPullRequest("git.exe", repository.Path));

        Assert.Equal(GitPullStatus.Failed, result.Status);
        Assert.Equal(BeforeSha, result.BeforeCommitSha);
        Assert.Contains("fast-forward", result.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(4, runner.Requests.Count);
        Assert.Equal(new[] { "pull", "--ff-only" }, runner.Requests[3].Arguments);
    }

    [Fact]
    public async Task PullAsync_fails_verification_if_branch_changes_during_pull()
    {
        using var repository = new TempRepository();
        var runner = new QueueProcessRunner(
            (_, _) => Success(StdOut("main")),
            (_, _) => Success(StdOut("origin/main")),
            (_, _) => Success(StdOut(BeforeSha)),
            (_, _) => Success(),
            (_, _) => Success(StdOut("other")));
        var service = new GitPullService(runner);

        var result = await service.PullAsync(
            new GitPullRequest("git.exe", repository.Path));

        Assert.Equal(GitPullStatus.VerificationFailed, result.Status);
        Assert.Equal("other", result.CurrentBranch);
        Assert.Equal(5, runner.Requests.Count);
    }

    [Fact]
    public async Task PullAsync_maps_pull_timeout_and_stops_before_verification()
    {
        using var repository = new TempRepository();
        var runner = new QueueProcessRunner(
            (_, _) => Success(StdOut("main")),
            (_, _) => Success(StdOut("origin/main")),
            (_, _) => Success(StdOut(BeforeSha)),
            (_, _) => Failure(
                ProcessExecutionStatus.TimedOut,
                null,
                "Process timed out."));
        var service = new GitPullService(runner);

        var result = await service.PullAsync(
            new GitPullRequest(
                "git.exe",
                repository.Path,
                TimeSpan.FromSeconds(5)));

        Assert.Equal(GitPullStatus.TimedOut, result.Status);
        Assert.Equal(4, runner.Requests.Count);
        Assert.All(runner.Requests, request => Assert.Equal(TimeSpan.FromSeconds(5), request.Timeout));
    }

    [Fact]
    public async Task PullAsync_honors_pre_cancelled_operation_without_running_git()
    {
        using var repository = new TempRepository();
        var runner = new QueueProcessRunner(
            (_, _) => throw new InvalidOperationException("Git must not run."));
        var service = new GitPullService(runner);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await service.PullAsync(
            new GitPullRequest("git.exe", repository.Path),
            cancellationToken: cancellation.Token);

        Assert.Equal(GitPullStatus.Cancelled, result.Status);
        Assert.Empty(runner.Requests);
    }

    private static QueueProcessRunner SuccessfulRunner(string beforeSha, string afterSha)
        => new(
            (_, _) => Success(StdOut("main")),
            (_, _) => Success(StdOut("origin/main")),
            (_, _) => Success(StdOut(beforeSha)),
            (_, _) => Success(),
            (_, _) => Success(StdOut("main")),
            (_, _) => Success(StdOut(afterSha)));

    private static ProcessOutputLine StdOut(string text)
        => new(DateTimeOffset.UtcNow, ProcessStream.StdOut, text);

    private static ProcessOutputLine StdErr(string text)
        => new(DateTimeOffset.UtcNow, ProcessStream.StdErr, text);

    private static ProcessResult Success(params ProcessOutputLine[] output)
        => CreateResult(ProcessExecutionStatus.Succeeded, 0, null, output);

    private static ProcessResult Failure(
        ProcessExecutionStatus status,
        int? exitCode,
        string failureReason,
        params ProcessOutputLine[] output)
        => CreateResult(status, exitCode, failureReason, output);

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
            "git pull test",
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

    private sealed class TempRepository : IDisposable
    {
        public TempRepository()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "FlutterBuildDoctor.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
            Directory.CreateDirectory(System.IO.Path.Combine(Path, ".git"));
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
