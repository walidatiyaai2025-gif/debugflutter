using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Git.Branches;

namespace FlutterBuildDoctor.UnitTests.Git;

public sealed class GitBranchSwitcherTests
{
    [Fact]
    public async Task SwitchAsync_switches_local_branch_and_verifies_current_branch_and_head()
    {
        using var repository = new TempRepository();
        var progressLine = StdErr("Switched to branch 'feature/ui'");
        var runner = new QueueProcessRunner(
            (_, progress) =>
            {
                progress?.Report(progressLine);
                return Success();
            },
            (_, _) => Success(StdOut("feature/ui")),
            (_, _) => Success(StdOut("1111111111111111111111111111111111111111")));
        var switcher = new GitBranchSwitcher(runner);
        var progress = new CapturingProgress();
        var branch = new GitBranchInfo(
            "feature/ui",
            "refs/heads/feature/ui",
            GitBranchKind.Local,
            "1111111111111111111111111111111111111111");

        var result = await switcher.SwitchAsync(
            new GitBranchSwitchRequest("git.exe", repository.Path, branch),
            progress);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(GitBranchSwitchStatus.Succeeded, result.Status);
        Assert.Equal("feature/ui", result.CurrentBranch);
        Assert.Equal("1111111111111111111111111111111111111111", result.CommitSha);
        Assert.Equal(progressLine, Assert.Single(progress.Lines));
        Assert.Equal(3, runner.Requests.Count);
        Assert.Equal(new[] { "switch", "--", "feature/ui" }, runner.Requests[0].Arguments);
        Assert.Equal(new[] { "branch", "--show-current" }, runner.Requests[1].Arguments);
        Assert.Equal(new[] { "rev-parse", "--verify", "HEAD" }, runner.Requests[2].Arguments);
        Assert.Equal(repository.Path, runner.Requests[0].WorkingDirectory);
        Assert.NotNull(runner.Requests[0].Environment);
        Assert.Equal("0", runner.Requests[0].Environment!["GIT_TERMINAL_PROMPT"]);
        Assert.Equal("Never", runner.Requests[0].Environment!["GCM_INTERACTIVE"]);
        Assert.DoesNotContain("--force", runner.Requests[0].Arguments);
        Assert.DoesNotContain("--discard-changes", runner.Requests[0].Arguments);
    }

    [Fact]
    public async Task SwitchAsync_creates_tracking_branch_from_selected_remote_ref()
    {
        using var repository = new TempRepository();
        var runner = new QueueProcessRunner(
            (_, _) => Success(),
            (_, _) => Success(StdOut("feature/api")),
            (_, _) => Success(StdOut("2222222222222222222222222222222222222222")));
        var switcher = new GitBranchSwitcher(runner);
        var branch = new GitBranchInfo(
            "feature/api",
            "refs/remotes/origin/feature/api",
            GitBranchKind.Remote,
            "2222222222222222222222222222222222222222",
            RemoteName: "origin");

        var result = await switcher.SwitchAsync(
            new GitBranchSwitchRequest("git.exe", repository.Path, branch));

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(
            new[] { "switch", "--track", "--", "origin/feature/api" },
            runner.Requests[0].Arguments);
        Assert.Equal("feature/api", result.CurrentBranch);
    }

    [Fact]
    public async Task SwitchAsync_does_not_force_overwrite_when_git_blocks_local_changes()
    {
        using var repository = new TempRepository();
        var runner = new QueueProcessRunner(
            (_, _) => Failure(
                ProcessExecutionStatus.Failed,
                1,
                "git switch failed",
                StdErr("error: Your local changes would be overwritten by checkout")));
        var switcher = new GitBranchSwitcher(runner);
        var branch = new GitBranchInfo(
            "release",
            "refs/heads/release",
            GitBranchKind.Local,
            "3333333333333333333333333333333333333333");

        var result = await switcher.SwitchAsync(
            new GitBranchSwitchRequest("git.exe", repository.Path, branch));

        Assert.Equal(GitBranchSwitchStatus.Failed, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Contains("local changes", result.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Single(runner.Requests);
        Assert.DoesNotContain("--force", runner.Requests[0].Arguments);
        Assert.DoesNotContain("--discard-changes", runner.Requests[0].Arguments);
    }

    [Fact]
    public async Task SwitchAsync_fails_verification_when_git_reports_a_different_current_branch()
    {
        using var repository = new TempRepository();
        var runner = new QueueProcessRunner(
            (_, _) => Success(),
            (_, _) => Success(StdOut("main")));
        var switcher = new GitBranchSwitcher(runner);
        var branch = new GitBranchInfo(
            "feature/ui",
            "refs/heads/feature/ui",
            GitBranchKind.Local,
            "4444444444444444444444444444444444444444");

        var result = await switcher.SwitchAsync(
            new GitBranchSwitchRequest("git.exe", repository.Path, branch));

        Assert.Equal(GitBranchSwitchStatus.VerificationFailed, result.Status);
        Assert.Equal("main", result.CurrentBranch);
        Assert.Contains("expected 'feature/ui'", result.Message!, StringComparison.Ordinal);
        Assert.Equal(2, runner.Requests.Count);
    }

    [Fact]
    public async Task SwitchAsync_maps_process_timeout_and_stops_before_verification()
    {
        using var repository = new TempRepository();
        var runner = new QueueProcessRunner(
            (_, _) => Failure(
                ProcessExecutionStatus.TimedOut,
                null,
                "Process timed out."));
        var switcher = new GitBranchSwitcher(runner);
        var branch = new GitBranchInfo(
            "main",
            "refs/heads/main",
            GitBranchKind.Local,
            "5555555555555555555555555555555555555555");

        var result = await switcher.SwitchAsync(
            new GitBranchSwitchRequest(
                "git.exe",
                repository.Path,
                branch,
                TimeSpan.FromSeconds(4)));

        Assert.Equal(GitBranchSwitchStatus.TimedOut, result.Status);
        Assert.Single(runner.Requests);
        Assert.Equal(TimeSpan.FromSeconds(4), runner.Requests[0].Timeout);
    }

    [Fact]
    public async Task SwitchAsync_rejects_inconsistent_remote_branch_metadata_before_running_git()
    {
        using var repository = new TempRepository();
        var runner = new QueueProcessRunner(
            (_, _) => throw new InvalidOperationException("Git must not run."));
        var switcher = new GitBranchSwitcher(runner);
        var branch = new GitBranchInfo(
            "feature/ui",
            "refs/remotes/upstream/feature/ui",
            GitBranchKind.Remote,
            "6666666666666666666666666666666666666666",
            RemoteName: "origin");

        var result = await switcher.SwitchAsync(
            new GitBranchSwitchRequest("git.exe", repository.Path, branch));

        Assert.Equal(GitBranchSwitchStatus.InvalidBranch, result.Status);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task SwitchAsync_rejects_non_git_folder_before_running_git()
    {
        using var directory = new TempDirectory(createGitMetadata: false);
        var runner = new QueueProcessRunner(
            (_, _) => throw new InvalidOperationException("Git must not run."));
        var switcher = new GitBranchSwitcher(runner);
        var branch = new GitBranchInfo(
            "main",
            "refs/heads/main",
            GitBranchKind.Local,
            "7777777777777777777777777777777777777777");

        var result = await switcher.SwitchAsync(
            new GitBranchSwitchRequest("git.exe", directory.Path, branch));

        Assert.Equal(GitBranchSwitchStatus.InvalidRepository, result.Status);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task SwitchAsync_honors_pre_cancelled_operation_without_running_git()
    {
        using var repository = new TempRepository();
        var runner = new QueueProcessRunner(
            (_, _) => throw new InvalidOperationException("Git must not run."));
        var switcher = new GitBranchSwitcher(runner);
        var branch = new GitBranchInfo(
            "main",
            "refs/heads/main",
            GitBranchKind.Local,
            "8888888888888888888888888888888888888888");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await switcher.SwitchAsync(
            new GitBranchSwitchRequest("git.exe", repository.Path, branch),
            cancellationToken: cancellation.Token);

        Assert.Equal(GitBranchSwitchStatus.Cancelled, result.Status);
        Assert.Empty(runner.Requests);
    }

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
            "git branch switch test",
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
