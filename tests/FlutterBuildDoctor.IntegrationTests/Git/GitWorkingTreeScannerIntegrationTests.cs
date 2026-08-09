using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Git.Repository;
using FlutterBuildDoctor.Infrastructure.Environment;
using FlutterBuildDoctor.Infrastructure.Processes;

namespace FlutterBuildDoctor.IntegrationTests.Git;

public sealed class GitWorkingTreeScannerIntegrationTests
{
    [Fact]
    public async Task ScanAsync_detects_real_dirty_states_without_mutating_repository()
    {
        Assert.True(OperatingSystem.IsWindows());
        using var repository = await GitRepositoryFixture.CreateAsync();
        var scanner = new GitWorkingTreeScanner(repository.Runner);

        var clean = await scanner.ScanAsync(
            new GitWorkingTreeScanRequest(
                repository.GitPath,
                repository.Path,
                TimeSpan.FromSeconds(30)));

        Assert.True(clean.IsSuccess, clean.Message);
        Assert.True(clean.IsClean);
        Assert.Empty(clean.Changes);

        var headBefore = await repository.RevParseAsync("HEAD");
        var trackedPath = System.IO.Path.Combine(repository.Path, "tracked.txt");
        var stagedPath = System.IO.Path.Combine(repository.Path, "staged.txt");
        var untrackedDirectory = System.IO.Path.Combine(repository.Path, "folder");
        var untrackedPath = System.IO.Path.Combine(untrackedDirectory, "new file.txt");
        var renamedPath = System.IO.Path.Combine(repository.Path, "renamed file.txt");

        File.WriteAllText(trackedPath, "unstaged modification");
        File.WriteAllText(stagedPath, "staged addition");
        await repository.RunGitAsync("add", "--", "staged.txt");
        Directory.CreateDirectory(untrackedDirectory);
        File.WriteAllText(untrackedPath, "untracked content");
        await repository.RunGitAsync("mv", "--", "rename-me.txt", "renamed file.txt");

        var result = await scanner.ScanAsync(
            new GitWorkingTreeScanRequest(
                repository.GitPath,
                repository.Path,
                TimeSpan.FromSeconds(30)));

        Assert.True(result.IsSuccess, result.Message);
        Assert.True(result.IsDirty);
        Assert.False(string.IsNullOrEmpty(result.RawStatus));

        var modified = Assert.Single(result.Changes, change => change.Path == "tracked.txt");
        Assert.Equal(GitWorkingTreeChangeKind.Modified, modified.Kind);
        Assert.False(modified.IsStaged);
        Assert.True(modified.IsUnstaged);

        var added = Assert.Single(result.Changes, change => change.Path == "staged.txt");
        Assert.Equal(GitWorkingTreeChangeKind.Added, added.Kind);
        Assert.True(added.IsStaged);
        Assert.False(added.IsUnstaged);

        var untracked = Assert.Single(result.Changes, change => change.Path == "folder/new file.txt");
        Assert.Equal(GitWorkingTreeChangeKind.Untracked, untracked.Kind);
        Assert.False(untracked.IsStaged);
        Assert.True(untracked.IsUnstaged);

        var renamed = Assert.Single(result.Changes, change => change.Path == "renamed file.txt");
        Assert.Equal(GitWorkingTreeChangeKind.Renamed, renamed.Kind);
        Assert.Equal("rename-me.txt", renamed.OriginalPath);
        Assert.True(renamed.IsStaged);

        Assert.Equal(headBefore, await repository.RevParseAsync("HEAD"));
        Assert.Equal("unstaged modification", File.ReadAllText(trackedPath));
        Assert.Equal("staged addition", File.ReadAllText(stagedPath));
        Assert.Equal("untracked content", File.ReadAllText(untrackedPath));
        Assert.True(File.Exists(renamedPath));
        Assert.False(File.Exists(System.IO.Path.Combine(repository.Path, "rename-me.txt")));
    }

    private sealed class GitRepositoryFixture : IDisposable
    {
        private GitRepositoryFixture(string gitPath, IProcessRunner runner, string path)
        {
            GitPath = gitPath;
            Runner = runner;
            Path = path;
        }

        public string GitPath { get; }

        public IProcessRunner Runner { get; }

        public string Path { get; }

        public static async Task<GitRepositoryFixture> CreateAsync()
        {
            var runner = new ProcessRunner();
            var detector = new GitToolDetector(runner);
            var git = await detector.DetectAsync();
            Assert.True(git.Installed, git.Message);
            Assert.False(string.IsNullOrWhiteSpace(git.Path));

            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "FlutterBuildDoctor.WorkingTreeIntegrationTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);

            var fixture = new GitRepositoryFixture(git.Path!, runner, path);
            try
            {
                await fixture.RunGitAsync("init");
                await fixture.RunGitAsync("config", "user.name", "Flutter Build Doctor Tests");
                await fixture.RunGitAsync(
                    "config",
                    "user.email",
                    "flutter-build-doctor@example.invalid");
                await fixture.RunGitAsync("config", "core.autocrlf", "false");

                File.WriteAllText(System.IO.Path.Combine(path, "tracked.txt"), "initial tracked");
                File.WriteAllText(System.IO.Path.Combine(path, "rename-me.txt"), "rename source");
                await fixture.RunGitAsync("add", "--", "tracked.txt", "rename-me.txt");
                await fixture.RunGitAsync("commit", "-m", "initial");
                await fixture.RunGitAsync("branch", "-M", "main");
                return fixture;
            }
            catch
            {
                fixture.Dispose();
                throw;
            }
        }

        public async Task<string> RevParseAsync(string reference)
        {
            var result = await RunGitAsync("rev-parse", "--verify", reference);
            var output = result.Output.FirstOrDefault(static line =>
                line.Stream == ProcessStream.StdOut &&
                !string.IsNullOrWhiteSpace(line.Text));
            Assert.NotNull(output);
            return output.Text.Trim();
        }

        public async Task<ProcessResult> RunGitAsync(params string[] arguments)
        {
            var result = await Runner.RunAsync(
                new ProcessRequest(
                    GitPath,
                    arguments,
                    WorkingDirectory: Path,
                    Environment: new Dictionary<string, string?>
                    {
                        ["GIT_TERMINAL_PROMPT"] = "0",
                        ["GCM_INTERACTIVE"] = "Never"
                    },
                    Timeout: TimeSpan.FromSeconds(30),
                    DisplayName: "Git working-tree integration fixture"));

            Assert.True(
                result.IsSuccess,
                $"Git command failed: git {string.Join(' ', arguments)} — {FailureEvidence(result)}");
            return result;
        }

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

        private static string FailureEvidence(ProcessResult result)
            => result.Output.LastOrDefault(static line =>
                    line.Stream == ProcessStream.StdErr &&
                    !string.IsNullOrWhiteSpace(line.Text))
                ?.Text
                ?? result.FailureReason
                ?? "unknown Git failure";
    }
}
