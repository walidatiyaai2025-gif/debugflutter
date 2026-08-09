using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Git.Branches;
using FlutterBuildDoctor.Infrastructure.Environment;
using FlutterBuildDoctor.Infrastructure.Processes;

namespace FlutterBuildDoctor.IntegrationTests.Git;

public sealed class GitPullServiceIntegrationTests
{
    [Fact]
    public async Task PullAsync_reports_up_to_date_with_real_git()
    {
        Assert.True(OperatingSystem.IsWindows());
        using var fixture = await GitPullFixture.CreateAsync();
        var before = await fixture.RevParseAsync(fixture.ClientPath, "HEAD");
        var service = new GitPullService(fixture.Runner);

        var result = await service.PullAsync(
            new GitPullRequest(
                fixture.GitPath,
                fixture.ClientPath,
                TimeSpan.FromSeconds(30)));

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(GitPullStatus.UpToDate, result.Status);
        Assert.Equal("main", result.CurrentBranch);
        Assert.Equal("origin/main", result.Upstream);
        Assert.Equal(before, result.BeforeCommitSha);
        Assert.Equal(before, result.AfterCommitSha);
    }

    [Fact]
    public async Task PullAsync_fast_forwards_to_new_remote_commit_with_real_git()
    {
        Assert.True(OperatingSystem.IsWindows());
        using var fixture = await GitPullFixture.CreateAsync();
        var clientBefore = await fixture.RevParseAsync(fixture.ClientPath, "HEAD");

        File.WriteAllText(
            System.IO.Path.Combine(fixture.PublisherPath, "tracked.txt"),
            "remote update");
        await fixture.RunGitAsync(fixture.PublisherPath, "add", "--", "tracked.txt");
        await fixture.RunGitAsync(fixture.PublisherPath, "commit", "-m", "remote update");
        await fixture.RunGitAsync(fixture.PublisherPath, "push", "origin", "main");
        var remoteCommit = await fixture.RevParseAsync(fixture.PublisherPath, "HEAD");

        var service = new GitPullService(fixture.Runner);
        var result = await service.PullAsync(
            new GitPullRequest(
                fixture.GitPath,
                fixture.ClientPath,
                TimeSpan.FromSeconds(30)));

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(GitPullStatus.FastForwarded, result.Status);
        Assert.True(result.Changed);
        Assert.Equal(clientBefore, result.BeforeCommitSha);
        Assert.Equal(remoteCommit, result.AfterCommitSha);
        Assert.Equal(
            "remote update",
            File.ReadAllText(System.IO.Path.Combine(fixture.ClientPath, "tracked.txt")));
    }

    [Fact]
    public async Task PullAsync_refuses_diverged_history_without_merging_or_rebasing()
    {
        Assert.True(OperatingSystem.IsWindows());
        using var fixture = await GitPullFixture.CreateAsync();

        File.WriteAllText(
            System.IO.Path.Combine(fixture.ClientPath, "client.txt"),
            "client commit");
        await fixture.RunGitAsync(fixture.ClientPath, "add", "--", "client.txt");
        await fixture.RunGitAsync(fixture.ClientPath, "commit", "-m", "client commit");
        var clientBefore = await fixture.RevParseAsync(fixture.ClientPath, "HEAD");

        File.WriteAllText(
            System.IO.Path.Combine(fixture.PublisherPath, "publisher.txt"),
            "publisher commit");
        await fixture.RunGitAsync(fixture.PublisherPath, "add", "--", "publisher.txt");
        await fixture.RunGitAsync(fixture.PublisherPath, "commit", "-m", "publisher commit");
        await fixture.RunGitAsync(fixture.PublisherPath, "push", "origin", "main");

        var service = new GitPullService(fixture.Runner);
        var result = await service.PullAsync(
            new GitPullRequest(
                fixture.GitPath,
                fixture.ClientPath,
                TimeSpan.FromSeconds(30)));

        Assert.Equal(GitPullStatus.Failed, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Contains("fast-forward", result.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(clientBefore, await fixture.RevParseAsync(fixture.ClientPath, "HEAD"));
        Assert.Equal("main", await fixture.CurrentBranchAsync(fixture.ClientPath));
        Assert.True(File.Exists(System.IO.Path.Combine(fixture.ClientPath, "client.txt")));
    }

    private sealed class GitPullFixture : IDisposable
    {
        private GitPullFixture(string gitPath, IProcessRunner runner, string rootPath)
        {
            GitPath = gitPath;
            Runner = runner;
            RootPath = rootPath;
            RemotePath = System.IO.Path.Combine(rootPath, "remote.git");
            PublisherPath = System.IO.Path.Combine(rootPath, "publisher");
            ClientPath = System.IO.Path.Combine(rootPath, "client");
        }

        public string GitPath { get; }

        public IProcessRunner Runner { get; }

        public string RootPath { get; }

        public string RemotePath { get; }

        public string PublisherPath { get; }

        public string ClientPath { get; }

        public static async Task<GitPullFixture> CreateAsync()
        {
            var runner = new ProcessRunner();
            var detector = new GitToolDetector(runner);
            var git = await detector.DetectAsync();
            Assert.True(git.Installed, git.Message);
            Assert.False(string.IsNullOrWhiteSpace(git.Path));

            var root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "FlutterBuildDoctor.PullIntegrationTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            var fixture = new GitPullFixture(git.Path!, runner, root);
            try
            {
                await fixture.RunGitAsync(root, "init", "--bare", "--initial-branch=main", "--", fixture.RemotePath);
                Directory.CreateDirectory(fixture.PublisherPath);
                await fixture.RunGitAsync(fixture.PublisherPath, "init");
                await fixture.ConfigureIdentityAsync(fixture.PublisherPath);
                await fixture.RunGitAsync(fixture.PublisherPath, "branch", "-M", "main");

                File.WriteAllText(
                    System.IO.Path.Combine(fixture.PublisherPath, "tracked.txt"),
                    "initial content");
                await fixture.RunGitAsync(fixture.PublisherPath, "add", "--", "tracked.txt");
                await fixture.RunGitAsync(fixture.PublisherPath, "commit", "-m", "initial");
                await fixture.RunGitAsync(fixture.PublisherPath, "remote", "add", "origin", fixture.RemotePath);
                await fixture.RunGitAsync(fixture.PublisherPath, "push", "-u", "origin", "main");

                await fixture.RunGitAsync(root, "clone", "--", fixture.RemotePath, fixture.ClientPath);
                await fixture.ConfigureIdentityAsync(fixture.ClientPath);
                return fixture;
            }
            catch
            {
                fixture.Dispose();
                throw;
            }
        }

        public async Task<string> RevParseAsync(string workingDirectory, string reference)
        {
            var result = await RunGitAsync(workingDirectory, "rev-parse", "--verify", reference);
            return FirstStdOut(result);
        }

        public async Task<string> CurrentBranchAsync(string workingDirectory)
        {
            var result = await RunGitAsync(workingDirectory, "branch", "--show-current");
            return FirstStdOut(result);
        }

        public async Task<ProcessResult> RunGitAsync(
            string workingDirectory,
            params string[] arguments)
        {
            var result = await Runner.RunAsync(
                new ProcessRequest(
                    GitPath,
                    arguments,
                    WorkingDirectory: workingDirectory,
                    Environment: new Dictionary<string, string?>
                    {
                        ["GIT_TERMINAL_PROMPT"] = "0",
                        ["GCM_INTERACTIVE"] = "Never"
                    },
                    Timeout: TimeSpan.FromSeconds(30),
                    DisplayName: "Git pull integration fixture"));

            Assert.True(
                result.IsSuccess,
                $"Git command failed: git {string.Join(' ', arguments)} — {FailureEvidence(result)}");
            return result;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootPath))
                {
                    Directory.Delete(RootPath, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup for test temp data.
            }
        }

        private async Task ConfigureIdentityAsync(string workingDirectory)
        {
            await RunGitAsync(workingDirectory, "config", "user.name", "Flutter Build Doctor Tests");
            await RunGitAsync(
                workingDirectory,
                "config",
                "user.email",
                "flutter-build-doctor@example.invalid");
            await RunGitAsync(workingDirectory, "config", "core.autocrlf", "false");
        }

        private static string FirstStdOut(ProcessResult result)
        {
            var output = result.Output.FirstOrDefault(static line =>
                line.Stream == ProcessStream.StdOut &&
                !string.IsNullOrWhiteSpace(line.Text));
            Assert.NotNull(output);
            return output.Text.Trim();
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
