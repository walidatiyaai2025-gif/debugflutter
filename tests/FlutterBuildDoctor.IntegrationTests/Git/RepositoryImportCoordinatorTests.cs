using FlutterBuildDoctor.App.Services;
using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Git.Branches;
using FlutterBuildDoctor.Git.Cloning;
using FlutterBuildDoctor.Git.Repository;

namespace FlutterBuildDoctor.IntegrationTests.Git;

public sealed class RepositoryImportCoordinatorTests
{
    [Fact]
    public async Task ImportAsync_NewRepository_UsesCloneThenKeepsRequestedCurrentLocalBranch()
    {
        var clone = new StubCloneService(_ =>
            new GitCloneResult(GitCloneStatus.Succeeded, @"C:\workspace\repo", "cloned"));
        var refresh = new StubRefreshService(_ => throw new InvalidOperationException("Refresh must not run."));
        var branches = new StubBranchService(_ => SuccessBranches(
            new GitBranchInfo("main", "refs/heads/main", GitBranchKind.Local, "abc", IsCurrent: true),
            new GitBranchInfo("main", "refs/remotes/origin/main", GitBranchKind.Remote, "abc", RemoteName: "origin")));
        var switcher = new StubBranchSwitcher(_ => throw new InvalidOperationException("Switch must not run for current branch."));
        var coordinator = new RepositoryImportCoordinator(clone, refresh, branches, switcher);

        var result = await coordinator.ImportAsync(Request("main"));

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(@"C:\workspace\repo", result.RepositoryPath);
        Assert.Equal(1, clone.CallCount);
        Assert.Equal(0, refresh.CallCount);
        Assert.Equal(1, branches.CallCount);
        Assert.Equal(0, switcher.CallCount);
    }

    [Fact]
    public async Task ImportAsync_ExistingGitRepository_UsesSafeRefreshThenSwitchesRemoteBranch()
    {
        using var repository = new TempGitDirectory();
        var backupPath = repository.Path + ".backup";
        var clone = new StubCloneService(_ =>
            new GitCloneResult(GitCloneStatus.TargetNotEmpty, repository.Path, "exists"));
        var refresh = new StubRefreshService(request =>
            new GitRepositoryRefreshResult(
                GitRepositoryRefreshStatus.Succeeded,
                request.RepositoryPath,
                BackupPath: backupPath,
                Message: "refreshed"));
        var remote = new GitBranchInfo(
            "feature/ui",
            "refs/remotes/origin/feature/ui",
            GitBranchKind.Remote,
            "def",
            RemoteName: "origin");
        var branches = new StubBranchService(_ => SuccessBranches(remote));
        var switcher = new StubBranchSwitcher(request =>
            new GitBranchSwitchResult(
                GitBranchSwitchStatus.Succeeded,
                request.Branch.Name,
                request.Branch.CommitSha,
                "switched"));
        var coordinator = new RepositoryImportCoordinator(clone, refresh, branches, switcher);

        var result = await coordinator.ImportAsync(Request("feature/ui"));

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(repository.Path, result.RepositoryPath);
        Assert.Equal(backupPath, result.BackupPath);
        Assert.Equal(1, refresh.CallCount);
        Assert.Equal(1, switcher.CallCount);
        Assert.Same(remote, switcher.LastRequest!.Branch);
    }

    [Fact]
    public async Task ImportAsync_ExistingNonGitTarget_IsRejectedWithoutRefresh()
    {
        using var directory = new TempDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "keep.txt"), "keep");
        var clone = new StubCloneService(_ =>
            new GitCloneResult(GitCloneStatus.TargetNotEmpty, directory.Path, "exists"));
        var refresh = new StubRefreshService(_ => throw new InvalidOperationException("Refresh must not run."));
        var coordinator = new RepositoryImportCoordinator(
            clone,
            refresh,
            new StubBranchService(_ => throw new InvalidOperationException()),
            new StubBranchSwitcher(_ => throw new InvalidOperationException()));

        var result = await coordinator.ImportAsync(Request("main"));

        Assert.Equal(RepositoryImportStatus.ExistingTargetIsNotGit, result.Status);
        Assert.Equal(0, refresh.CallCount);
        Assert.True(File.Exists(Path.Combine(directory.Path, "keep.txt")));
    }

    [Fact]
    public async Task ImportAsync_PrefersExactLocalBranchOverRemoteMatch()
    {
        var local = new GitBranchInfo("develop", "refs/heads/develop", GitBranchKind.Local, "111");
        var remote = new GitBranchInfo("develop", "refs/remotes/origin/develop", GitBranchKind.Remote, "222", RemoteName: "origin");
        var switcher = new StubBranchSwitcher(request =>
            new GitBranchSwitchResult(GitBranchSwitchStatus.Succeeded, request.Branch.Name, request.Branch.CommitSha));
        var coordinator = new RepositoryImportCoordinator(
            new StubCloneService(_ => new GitCloneResult(GitCloneStatus.Succeeded, @"C:\workspace\repo", "cloned")),
            new StubRefreshService(_ => throw new InvalidOperationException()),
            new StubBranchService(_ => SuccessBranches(remote, local)),
            switcher);

        var result = await coordinator.ImportAsync(Request("develop"));

        Assert.True(result.IsSuccess, result.Message);
        Assert.Same(local, switcher.LastRequest!.Branch);
    }

    [Fact]
    public async Task ImportAsync_MissingRequestedBranch_ReturnsClearFailureWithoutSwitch()
    {
        var switcher = new StubBranchSwitcher(_ => throw new InvalidOperationException("Switch must not run."));
        var coordinator = new RepositoryImportCoordinator(
            new StubCloneService(_ => new GitCloneResult(GitCloneStatus.Succeeded, @"C:\workspace\repo", "cloned")),
            new StubRefreshService(_ => throw new InvalidOperationException()),
            new StubBranchService(_ => SuccessBranches(
                new GitBranchInfo("main", "refs/heads/main", GitBranchKind.Local, "111", IsCurrent: true))),
            switcher);

        var result = await coordinator.ImportAsync(Request("missing"));

        Assert.Equal(RepositoryImportStatus.BranchNotFound, result.Status);
        Assert.Contains("missing", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, switcher.CallCount);
    }

    private static RepositoryImportRequest Request(string branch)
        => new("git.exe", "https://github.com/example/repo.git", branch, @"C:\workspace", TimeSpan.FromSeconds(30));

    private static GitBranchDiscoveryResult SuccessBranches(params GitBranchInfo[] branches)
        => new(GitBranchDiscoveryStatus.Succeeded, branches, "branches");

    private sealed class StubCloneService : IGitCloneService
    {
        private readonly Func<GitCloneRequest, GitCloneResult> _handler;

        public StubCloneService(Func<GitCloneRequest, GitCloneResult> handler) => _handler = handler;
        public int CallCount { get; private set; }

        public Task<GitCloneResult> CloneAsync(GitCloneRequest request, IProgress<ProcessOutputLine>? progress = null, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_handler(request));
        }
    }

    private sealed class StubRefreshService : IGitRepositoryRefreshService
    {
        private readonly Func<GitRepositoryRefreshRequest, GitRepositoryRefreshResult> _handler;

        public StubRefreshService(Func<GitRepositoryRefreshRequest, GitRepositoryRefreshResult> handler) => _handler = handler;
        public int CallCount { get; private set; }

        public Task<GitRepositoryRefreshResult> RefreshAsync(GitRepositoryRefreshRequest request, IProgress<ProcessOutputLine>? progress = null, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_handler(request));
        }
    }

    private sealed class StubBranchService : IGitBranchService
    {
        private readonly Func<GitBranchDiscoveryRequest, GitBranchDiscoveryResult> _handler;

        public StubBranchService(Func<GitBranchDiscoveryRequest, GitBranchDiscoveryResult> handler) => _handler = handler;
        public int CallCount { get; private set; }

        public Task<GitBranchDiscoveryResult> GetBranchesAsync(GitBranchDiscoveryRequest request, IProgress<ProcessOutputLine>? progress = null, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_handler(request));
        }
    }

    private sealed class StubBranchSwitcher : IGitBranchSwitcher
    {
        private readonly Func<GitBranchSwitchRequest, GitBranchSwitchResult> _handler;

        public StubBranchSwitcher(Func<GitBranchSwitchRequest, GitBranchSwitchResult> handler) => _handler = handler;
        public int CallCount { get; private set; }
        public GitBranchSwitchRequest? LastRequest { get; private set; }

        public Task<GitBranchSwitchResult> SwitchAsync(GitBranchSwitchRequest request, IProgress<ProcessOutputLine>? progress = null, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_handler(request));
        }
    }

    private class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FlutterBuildDoctorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class TempGitDirectory : TempDirectory
    {
        public TempGitDirectory()
        {
            Directory.CreateDirectory(System.IO.Path.Combine(Path, ".git"));
        }
    }
}
