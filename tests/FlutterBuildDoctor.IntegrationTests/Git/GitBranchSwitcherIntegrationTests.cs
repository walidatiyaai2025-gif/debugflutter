using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Git.Branches;
using FlutterBuildDoctor.Infrastructure.Environment;
using FlutterBuildDoctor.Infrastructure.Processes;

namespace FlutterBuildDoctor.IntegrationTests.Git;

public sealed class GitBranchSwitcherIntegrationTests
{
    [Fact]
    public async Task SwitchAsync_switches_a_real_local_branch_with_the_production_process_runner()
    {
        Assert.True(OperatingSystem.IsWindows());
        using var repository = await CreateRepositoryAsync();
        var featureCommit = await RevParseAsync(repository, "feature/integration");
        var switcher = new GitBranchSwitcher(repository.Runner);
        var branch = new GitBranchInfo(
            "feature/integration",
            "refs/heads/feature/integration",
            GitBranchKind.Local,
            featureCommit);

        var result = await switcher.SwitchAsync(
            new GitBranchSwitchRequest(
                repository.GitPath,
                repository.Path,
                branch,
                TimeSpan.FromSeconds(30)));

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal("feature/integration", result.CurrentBranch);
        Assert.Equal(featureCommit, result.CommitSha);
    }

    [Fact]
    public async Task SwitchAsync_preserves_dirty_changes_when_git_blocks_the_switch()
    {
        Assert.True(OperatingSystem.IsWindows());
        using var repository = await CreateRepositoryAsync();
        var trackedFile = System.IO.Path.Combine(repository.Path, "tracked.txt");

        await RunGitAsync(repository, "switch", "-c", "feature/conflict");
        File.WriteAllText(trackedFile, "feature content");
        await RunGitAsync(repository, "add", "--", "tracked.txt");
        await RunGitAsync(repository, "commit", "-m", "feature content");
        var featureCommit = await RevParseAsync(repository, "feature/conflict");
        await RunGitAsync(repository, "switch", "main");

        File.WriteAllText(trackedFile, "dirty local content");

        var switcher = new GitBranchSwitcher(repository.Runner);
        var branch = new GitBranchInfo(
            "feature/conflict",
            "refs/heads/feature/conflict",
            GitBranchKind.Local,
            featureCommit);

        var result = await switcher.SwitchAsync(
            new GitBranchSwitchRequest(
                repository.GitPath,
                repository.Path,
                branch,
                TimeSpan.FromSeconds(30)));

        Assert.Equal(GitBranchSwitchStatus.Failed, result.Status);
        Assert.Equal("dirty local content", File.ReadAllText(trackedFile));
        Assert.Equal("main", await CurrentBranchAsync(repository));
    }

    [Fact]
    public async Task SwitchAsync_creates_a_real_tracking_branch_from_a_remote_ref()
    {
        Assert.True(OperatingSystem.IsWindows());
        using var repository = await CreateRepositoryAsync();
        using var remote = new TempDirectory();

        await RunGitAsync(repository, "init", "--bare", "--", remote.Path);
        await RunGitAsync(repository, "remote", "add", "origin", remote.Path);
        await RunGitAsync(
            repository,
            "push",
            "origin",
            "feature/integration:refs/heads/feature/remote");
        await RunGitAsync(repository, "fetch", "origin");

        var remoteCommit = await RevParseAsync(repository, "origin/feature/remote");
        var switcher = new GitBranchSwitcher(repository.Runner);
        var branch = new GitBranchInfo(
            "feature/remote",
            "refs/remotes/origin/feature/remote",
            GitBranchKind.Remote,
            remoteCommit,
            RemoteName: "origin");

        var result = await switcher.SwitchAsync(
            new GitBranchSwitchRequest(
                repository.GitPath,
                repository.Path,
                branch,
                TimeSpan.FromSeconds(30)));

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal("feature/remote", result.CurrentBranch);
        Assert.Equal(remoteCommit, result.CommitSha);

        var upstream = await RunGitAsync(
            repository,
            "rev-parse",
            "--abbrev-ref",
            "--symbolic-full-name",
            "@{upstream}");
        Assert.Equal("origin/feature/remote", FirstStdOut(upstream));
    }

    private static async Task<GitRepositoryFixture> CreateRepositoryAsync()
    {
        var runner = new ProcessRunner();
        var detector = new GitToolDetector(runner);
        var git = await detector.DetectAsync();
        Assert.True(git.Installed, git.Message);
        Assert.False(string.IsNullOrWhiteSpace(git.Path));

        var repository = new GitRepositoryFixture(git.Path!, runner);
        await RunGitAsync(repository, "init");
        await RunGitAsync(repository, "config", "user.name", "Flutter Build Doctor Tests");
        await RunGitAsync(repository, "config", "user.email", "flutter-build-doctor@example.invalid");
        await RunGitAsync(repository, "config", "core.autocrlf", "false");

        File.WriteAllText(System.IO.Path.Combine(repository.Path, "tracked.txt"), "initial content");
        await RunGitAsync(repository, "add", "--", "tracked.txt");
        await RunGitAsync(repository, "commit", "-m", "initial");
        await RunGitAsync(repository, "branch", "-M", "main");
        await RunGitAsync(repository, "branch", "feature/integration");
        return repository;
    }

    private static async Task<string> CurrentBranchAsync(GitRepositoryFixture repository)
    {
        var result = await RunGitAsync(repository, "branch", "--show-current");
        return FirstStdOut(result);
    }

    private static async Task<string> RevParseAsync(
        GitRepositoryFixture repository,
        string reference)
    {
        var result = await RunGitAsync(repository, "rev-parse", "--verify", reference);
        return FirstStdOut(result);
    }

    private static async Task<ProcessResult> RunGitAsync(
        GitRepositoryFixture repository,
        params string[] arguments)
    {
        var result = await repository.Runner.RunAsync(
            new ProcessRequest(
                repository.GitPath,
                arguments,
                WorkingDirectory: repository.Path,
                Environment: new Dictionary<string, string?>
                {
                    ["GIT_TERMINAL_PROMPT"] = "0",
                    ["GCM_INTERACTIVE"] = "Never"
                },
                Timeout: TimeSpan.FromSeconds(30),
                DisplayName: "Git integration fixture"));

        Assert.True(
            result.IsSuccess,
            $"Git command failed: git {string.Join(' ', arguments)} — {result.FailureReason}");
        return result;
    }

    private static string FirstStdOut(ProcessResult result)
    {
        var line = result.Output.FirstOrDefault(static output =>
            output.Stream == ProcessStream.StdOut &&
            !string.IsNullOrWhiteSpace(output.Text));

        Assert.NotNull(line);
        return line.Text.Trim();
    }

    private sealed class GitRepositoryFixture : IDisposable
    {
        public GitRepositoryFixture(string gitPath, IProcessRunner runner)
        {
            GitPath = gitPath;
            Runner = runner;
            Path = CreateTempPath();
            Directory.CreateDirectory(Path);
        }

        public string GitPath { get; }

        public IProcessRunner Runner { get; }

        public string Path { get; }

        public void Dispose() => DeleteDirectory(Path);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = CreateTempPath();
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => DeleteDirectory(Path);
    }

    private static string CreateTempPath()
        => System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "FlutterBuildDoctor.IntegrationTests",
            Guid.NewGuid().ToString("N"));

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup for test temp data.
        }
    }
}
