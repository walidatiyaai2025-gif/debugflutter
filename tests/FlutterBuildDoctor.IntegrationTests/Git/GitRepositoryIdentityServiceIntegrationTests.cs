using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Git.Repository;
using FlutterBuildDoctor.Infrastructure.Environment;
using FlutterBuildDoctor.Infrastructure.Processes;

namespace FlutterBuildDoctor.IntegrationTests.Git;

public sealed class GitRepositoryIdentityServiceIntegrationTests
{
    [Fact]
    public async Task ReadAsync_returns_real_branch_commit_upstream_and_remote_name()
    {
        Assert.True(OperatingSystem.IsWindows());
        using var fixture = await GitIdentityFixture.CreateAsync();
        var expectedCommit = await fixture.RevParseAsync("HEAD");
        var service = new GitRepositoryIdentityService(fixture.Runner);

        var result = await service.ReadAsync(
            new GitRepositoryIdentityRequest(
                fixture.GitPath,
                fixture.RepositoryPath,
                TimeSpan.FromSeconds(30)));

        Assert.True(result.IsSuccess, result.Message);
        Assert.NotNull(result.Identity);
        Assert.Equal("main", result.Identity!.BranchName);
        Assert.Equal(expectedCommit, result.Identity.CommitSha);
        Assert.Equal("origin/main", result.Identity.Upstream);
        Assert.Equal("origin", result.Identity.RemoteName);
        Assert.False(result.Identity.IsDetached);
    }

    [Fact]
    public async Task ReadAsync_reports_real_detached_head_without_tracking_identity()
    {
        Assert.True(OperatingSystem.IsWindows());
        using var fixture = await GitIdentityFixture.CreateAsync();
        var expectedCommit = await fixture.RevParseAsync("HEAD");
        await fixture.RunGitAsync("checkout", "--detach", expectedCommit);
        var service = new GitRepositoryIdentityService(fixture.Runner);

        var result = await service.ReadAsync(
            new GitRepositoryIdentityRequest(
                fixture.GitPath,
                fixture.RepositoryPath,
                TimeSpan.FromSeconds(30)));

        Assert.True(result.IsSuccess, result.Message);
        Assert.True(result.Identity!.IsDetached);
        Assert.Null(result.Identity.BranchName);
        Assert.Null(result.Identity.Upstream);
        Assert.Null(result.Identity.RemoteName);
        Assert.Equal(expectedCommit, result.Identity.CommitSha);
    }

    private sealed class GitIdentityFixture : IDisposable
    {
        private GitIdentityFixture(
            string gitPath,
            IProcessRunner runner,
            string rootPath,
            string repositoryPath,
            string remotePath)
        {
            GitPath = gitPath;
            Runner = runner;
            RootPath = rootPath;
            RepositoryPath = repositoryPath;
            RemotePath = remotePath;
        }

        public string GitPath { get; }

        public IProcessRunner Runner { get; }

        public string RootPath { get; }

        public string RepositoryPath { get; }

        public string RemotePath { get; }

        public static async Task<GitIdentityFixture> CreateAsync()
        {
            var runner = new ProcessRunner();
            var detector = new GitToolDetector(runner);
            var git = await detector.DetectAsync();
            Assert.True(git.Installed, git.Message);
            Assert.False(string.IsNullOrWhiteSpace(git.Path));

            var root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "FlutterBuildDoctor.IdentityIntegrationTests",
                Guid.NewGuid().ToString("N"));
            var repository = System.IO.Path.Combine(root, "repository");
            var remote = System.IO.Path.Combine(root, "origin.git");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(repository);

            var fixture = new GitIdentityFixture(git.Path!, runner, root, repository, remote);
            try
            {
                await fixture.RunGitAtAsync(root, "init", "--bare", "--initial-branch=main", "--", remote);
                await fixture.RunGitAsync("init");
                await fixture.RunGitAsync("config", "user.name", "Flutter Build Doctor Tests");
                await fixture.RunGitAsync(
                    "config",
                    "user.email",
                    "flutter-build-doctor@example.invalid");
                await fixture.RunGitAsync("config", "core.autocrlf", "false");
                await fixture.RunGitAsync("branch", "-M", "main");

                File.WriteAllText(System.IO.Path.Combine(repository, "tracked.txt"), "identity fixture");
                await fixture.RunGitAsync("add", "--", "tracked.txt");
                await fixture.RunGitAsync("commit", "-m", "initial");
                await fixture.RunGitAsync("remote", "add", "origin", remote);
                await fixture.RunGitAsync("push", "-u", "origin", "main");
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

        public Task<ProcessResult> RunGitAsync(params string[] arguments)
            => RunGitAtAsync(RepositoryPath, arguments);

        public async Task<ProcessResult> RunGitAtAsync(
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
                    DisplayName: "Git identity integration fixture"));

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

        private static string FailureEvidence(ProcessResult result)
            => result.Output.LastOrDefault(static line =>
                    line.Stream == ProcessStream.StdErr &&
                    !string.IsNullOrWhiteSpace(line.Text))
                ?.Text
                ?? result.FailureReason
                ?? "unknown Git failure";
    }
}
