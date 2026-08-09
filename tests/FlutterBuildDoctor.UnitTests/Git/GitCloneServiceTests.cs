using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Git.Cloning;
using FlutterBuildDoctor.Git.Validation;

namespace FlutterBuildDoctor.UnitTests.Git;

public sealed class GitCloneServiceTests
{
    [Fact]
    public async Task CloneAsync_builds_safe_clone_request_and_forwards_progress()
    {
        using var workspace = new TempDirectory();
        var progressLine = new ProcessOutputLine(
            DateTimeOffset.UtcNow,
            ProcessStream.StdErr,
            "Receiving objects: 50%");
        var runner = new RecordingProcessRunner((request, progress) =>
        {
            var targetName = request.Arguments[request.Arguments.Count - 1];
            Directory.CreateDirectory(Path.Combine(request.WorkingDirectory!, targetName, ".git"));
            progress?.Report(progressLine);
            return Result(ProcessExecutionStatus.Succeeded, 0);
        });
        var service = new GitCloneService(runner, new GitRepositoryUrlValidator());
        var progress = new CapturingProgress();

        var result = await service.CloneAsync(
            new GitCloneRequest(
                @"C:\Program Files\Git\cmd\git.exe",
                " https://github.com/example/sample.git/ ",
                workspace.Path),
            progress);

        Assert.True(result.IsSuccess);
        Assert.Equal(GitCloneStatus.Succeeded, result.Status);
        Assert.Equal(Path.Combine(workspace.Path, "sample"), result.RepositoryPath);
        Assert.Single(progress.Lines);
        Assert.Equal(progressLine, progress.Lines[0]);

        var processRequest = Assert.Single(runner.Requests);
        Assert.Equal(@"C:\Program Files\Git\cmd\git.exe", processRequest.FileName);
        Assert.Equal(
            new[] { "clone", "--progress", "--", "https://github.com/example/sample.git", "sample" },
            processRequest.Arguments);
        Assert.Equal(workspace.Path, processRequest.WorkingDirectory);
        Assert.Equal(TimeSpan.FromMinutes(15), processRequest.Timeout);
        Assert.Equal("0", processRequest.Environment!["GIT_TERMINAL_PROMPT"]);
        Assert.Equal("Never", processRequest.Environment["GCM_INTERACTIVE"]);
    }

    [Fact]
    public async Task CloneAsync_rejects_invalid_repository_url_before_starting_git()
    {
        using var workspace = new TempDirectory();
        var runner = new RecordingProcessRunner((_, _) =>
            throw new InvalidOperationException("Process runner must not be called."));
        var service = new GitCloneService(runner, new GitRepositoryUrlValidator());

        var result = await service.CloneAsync(
            new GitCloneRequest("git.exe", @"C:\source\repo", workspace.Path));

        Assert.Equal(GitCloneStatus.InvalidRepositoryUrl, result.Status);
        Assert.Contains("remote Git repository URL", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task CloneAsync_never_overwrites_a_non_empty_target_directory()
    {
        using var workspace = new TempDirectory();
        var target = Path.Combine(workspace.Path, "repo");
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(target, "keep.txt"), "preserve me");
        var runner = new RecordingProcessRunner((_, _) =>
            throw new InvalidOperationException("Process runner must not be called."));
        var service = new GitCloneService(runner, new GitRepositoryUrlValidator());

        var result = await service.CloneAsync(
            new GitCloneRequest(
                "git.exe",
                "https://github.com/example/repo.git",
                workspace.Path));

        Assert.Equal(GitCloneStatus.TargetNotEmpty, result.Status);
        Assert.True(File.Exists(Path.Combine(target, "keep.txt")));
        Assert.Empty(runner.Requests);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("nested/repo")]
    [InlineData("repo.")]
    [InlineData(" repo")]
    public async Task CloneAsync_rejects_unsafe_target_directory_names(string targetName)
    {
        using var workspace = new TempDirectory();
        var runner = new RecordingProcessRunner((_, _) =>
            throw new InvalidOperationException("Process runner must not be called."));
        var service = new GitCloneService(runner, new GitRepositoryUrlValidator());

        var result = await service.CloneAsync(
            new GitCloneRequest(
                "git.exe",
                "https://github.com/example/repo.git",
                workspace.Path,
                targetName));

        Assert.Equal(GitCloneStatus.InvalidTargetDirectory, result.Status);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task CloneAsync_maps_process_timeout_without_claiming_success()
    {
        using var workspace = new TempDirectory();
        var runner = new RecordingProcessRunner((_, _) =>
            Result(ProcessExecutionStatus.TimedOut, null, "Process timed out."));
        var service = new GitCloneService(runner, new GitRepositoryUrlValidator());

        var result = await service.CloneAsync(
            new GitCloneRequest(
                "git.exe",
                "git://example.com/team/repo.git",
                workspace.Path,
                Timeout: TimeSpan.FromSeconds(2)));

        Assert.Equal(GitCloneStatus.TimedOut, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Equal("Process timed out.", result.Message);
        Assert.Equal(TimeSpan.FromSeconds(2), Assert.Single(runner.Requests).Timeout);
    }

    [Fact]
    public async Task CloneAsync_verifies_git_metadata_after_zero_exit_code()
    {
        using var workspace = new TempDirectory();
        var runner = new RecordingProcessRunner((_, _) =>
            Result(ProcessExecutionStatus.Succeeded, 0));
        var service = new GitCloneService(runner, new GitRepositoryUrlValidator());

        var result = await service.CloneAsync(
            new GitCloneRequest(
                "git.exe",
                "git@example.com:team/repo.git",
                workspace.Path));

        Assert.Equal(GitCloneStatus.Failed, result.Status);
        Assert.Contains("does not contain Git metadata", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.ProcessResult);
    }

    [Fact]
    public async Task CloneAsync_creates_missing_workspace_and_accepts_existing_empty_target()
    {
        using var root = new TempDirectory();
        var workspacePath = Path.Combine(root.Path, "new-workspace");
        var targetPath = Path.Combine(workspacePath, "custom-repo");
        Directory.CreateDirectory(targetPath);
        var runner = new RecordingProcessRunner((request, _) =>
        {
            Directory.CreateDirectory(Path.Combine(targetPath, ".git"));
            return Result(ProcessExecutionStatus.Succeeded, 0);
        });
        var service = new GitCloneService(runner, new GitRepositoryUrlValidator());

        var result = await service.CloneAsync(
            new GitCloneRequest(
                "git.exe",
                "ssh://git@example.com/team/repo.git",
                workspacePath,
                "custom-repo"));

        Assert.True(result.IsSuccess);
        Assert.True(Directory.Exists(workspacePath));
        Assert.Equal(targetPath, result.RepositoryPath);
        Assert.Equal("custom-repo", Assert.Single(runner.Requests).Arguments.Last());
    }

    private static ProcessResult Result(
        ProcessExecutionStatus status,
        int? exitCode,
        string? failureReason = null)
    {
        var timestamp = DateTimeOffset.UtcNow;
        return new ProcessResult(
            status,
            exitCode,
            timestamp,
            timestamp,
            Array.Empty<ProcessOutputLine>(),
            "git clone",
            failureReason);
    }

    private sealed class RecordingProcessRunner : IProcessRunner
    {
        private readonly Func<ProcessRequest, IProgress<ProcessOutputLine>?, ProcessResult> _handler;

        public RecordingProcessRunner(
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

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "FlutterBuildDoctor.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
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
