using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Git.Repository;

namespace FlutterBuildDoctor.UnitTests.Git;

public sealed class GitWorkingTreeScannerTests
{
    [Fact]
    public async Task ScanAsync_reports_clean_tree_and_uses_read_only_porcelain_command()
    {
        using var repository = new TempRepository();
        var runner = new StubProcessRunner((_, _) => Success());
        var scanner = new GitWorkingTreeScanner(runner);

        var result = await scanner.ScanAsync(
            new GitWorkingTreeScanRequest("git.exe", repository.Path));

        Assert.True(result.IsSuccess, result.Message);
        Assert.True(result.IsClean);
        Assert.False(result.IsDirty);
        Assert.Empty(result.Changes);
        var request = Assert.Single(runner.Requests);
        Assert.Equal(
            new[] { "status", "--porcelain=v1", "-z", "--untracked-files=all" },
            request.Arguments);
        Assert.DoesNotContain("clean", request.Arguments);
        Assert.DoesNotContain("reset", request.Arguments);
        Assert.DoesNotContain("stash", request.Arguments);
        Assert.DoesNotContain("checkout", request.Arguments);
        Assert.DoesNotContain("switch", request.Arguments);
    }

    [Fact]
    public async Task ScanAsync_parses_staged_unstaged_untracked_and_unmerged_changes()
    {
        using var repository = new TempRepository();
        var raw = string.Concat(
            " M tracked.txt\0",
            "M  staged.txt\0",
            "?? folder/new file.txt\0",
            "UU conflict.txt\0");
        var runner = new StubProcessRunner((_, _) => Success(StdOut(raw)));
        var scanner = new GitWorkingTreeScanner(runner);

        var result = await scanner.ScanAsync(
            new GitWorkingTreeScanRequest("git.exe", repository.Path));

        Assert.True(result.IsSuccess, result.Message);
        Assert.True(result.IsDirty);
        Assert.Equal(4, result.Changes.Count);

        var tracked = Assert.Single(result.Changes, change => change.Path == "tracked.txt");
        Assert.Equal(GitWorkingTreeChangeKind.Modified, tracked.Kind);
        Assert.False(tracked.IsStaged);
        Assert.True(tracked.IsUnstaged);

        var staged = Assert.Single(result.Changes, change => change.Path == "staged.txt");
        Assert.Equal(GitWorkingTreeChangeKind.Modified, staged.Kind);
        Assert.True(staged.IsStaged);
        Assert.False(staged.IsUnstaged);

        var untracked = Assert.Single(result.Changes, change => change.Path == "folder/new file.txt");
        Assert.Equal(GitWorkingTreeChangeKind.Untracked, untracked.Kind);
        Assert.False(untracked.IsStaged);
        Assert.True(untracked.IsUnstaged);

        var unmerged = Assert.Single(result.Changes, change => change.Path == "conflict.txt");
        Assert.Equal(GitWorkingTreeChangeKind.Unmerged, unmerged.Kind);
        Assert.True(unmerged.IsStaged);
        Assert.True(unmerged.IsUnstaged);
    }

    [Fact]
    public async Task ScanAsync_parses_zero_terminated_rename_with_original_path()
    {
        using var repository = new TempRepository();
        var raw = "R  renamed file.txt\0original file.txt\0";
        var runner = new StubProcessRunner((_, _) => Success(StdOut(raw)));
        var scanner = new GitWorkingTreeScanner(runner);

        var result = await scanner.ScanAsync(
            new GitWorkingTreeScanRequest("git.exe", repository.Path));

        var change = Assert.Single(result.Changes);
        Assert.Equal(GitWorkingTreeChangeKind.Renamed, change.Kind);
        Assert.Equal("renamed file.txt", change.Path);
        Assert.Equal("original file.txt", change.OriginalPath);
        Assert.True(change.IsStaged);
    }

    [Fact]
    public async Task ScanAsync_preserves_raw_evidence_when_porcelain_output_is_malformed()
    {
        using var repository = new TempRepository();
        const string malformed = "this is not porcelain\0";
        var runner = new StubProcessRunner((_, _) => Success(StdOut(malformed)));
        var scanner = new GitWorkingTreeScanner(runner);

        var result = await scanner.ScanAsync(
            new GitWorkingTreeScanRequest("git.exe", repository.Path));

        Assert.Equal(GitWorkingTreeScanStatus.ParseFailed, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Equal(malformed, result.RawStatus);
        Assert.NotNull(result.ProcessResult);
        Assert.Empty(result.Changes);
    }

    [Fact]
    public async Task ScanAsync_forwards_progress_and_maps_timeout()
    {
        using var repository = new TempRepository();
        var progressLine = StdErr("status probe timed out");
        var runner = new StubProcessRunner((_, progress) =>
        {
            progress?.Report(progressLine);
            return Failure(ProcessExecutionStatus.TimedOut, null, "Process timed out.", progressLine);
        });
        var scanner = new GitWorkingTreeScanner(runner);
        var progress = new CapturingProgress();

        var result = await scanner.ScanAsync(
            new GitWorkingTreeScanRequest(
                "git.exe",
                repository.Path,
                TimeSpan.FromSeconds(4)),
            progress);

        Assert.Equal(GitWorkingTreeScanStatus.TimedOut, result.Status);
        Assert.Equal(progressLine, Assert.Single(progress.Lines));
        Assert.Equal(TimeSpan.FromSeconds(4), Assert.Single(runner.Requests).Timeout);
    }

    [Fact]
    public async Task ScanAsync_rejects_non_git_folder_before_running_git()
    {
        using var directory = new TempDirectory(createGitMetadata: false);
        var runner = new StubProcessRunner((_, _) =>
            throw new InvalidOperationException("Git must not run."));
        var scanner = new GitWorkingTreeScanner(runner);

        var result = await scanner.ScanAsync(
            new GitWorkingTreeScanRequest("git.exe", directory.Path));

        Assert.Equal(GitWorkingTreeScanStatus.InvalidRepository, result.Status);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task ScanAsync_honors_pre_cancelled_operation_without_running_git()
    {
        using var repository = new TempRepository();
        var runner = new StubProcessRunner((_, _) =>
            throw new InvalidOperationException("Git must not run."));
        var scanner = new GitWorkingTreeScanner(runner);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await scanner.ScanAsync(
            new GitWorkingTreeScanRequest("git.exe", repository.Path),
            cancellationToken: cancellation.Token);

        Assert.Equal(GitWorkingTreeScanStatus.Cancelled, result.Status);
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
            "git working tree scan test",
            failureReason);
    }

    private sealed class StubProcessRunner : IProcessRunner
    {
        private readonly Func<ProcessRequest, IProgress<ProcessOutputLine>?, ProcessResult> _handler;

        public StubProcessRunner(
            Func<ProcessRequest, IProgress<ProcessOutputLine>?, ProcessResult> handler)
        {
            _handler = handler;
        }

        public List<ProcessRequest> Requests { get; } = new();

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(_handler(request, progress));
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
