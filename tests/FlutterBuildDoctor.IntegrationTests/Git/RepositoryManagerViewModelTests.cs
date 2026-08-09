using FlutterBuildDoctor.App.Services;
using FlutterBuildDoctor.App.ViewModels;
using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Git.Branches;
using FlutterBuildDoctor.Git.Repository;

namespace FlutterBuildDoctor.IntegrationTests.Git;

public sealed class RepositoryManagerViewModelTests
{
    [Fact]
    public async Task ImportCommand_Success_LoadsProjectHeaderAndSurfacesBackupReceipt()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), "FlutterBuildDoctorTests", "repo-ui-success");
        var backupPath = repositoryPath + ".backup";
        var resolver = new StubGitResolver(new GitExecutableResolution(true, "git.exe", "2.55.0", "Git ready"));
        var coordinator = new StubImportCoordinator((request, progress) =>
        {
            progress?.Report(new ProcessOutputLine(DateTimeOffset.UtcNow, ProcessStream.StdErr, "Cloning into repository..."));
            return new RepositoryImportResult(
                RepositoryImportStatus.Succeeded,
                repositoryPath,
                backupPath,
                "Repository imported successfully.");
        });
        var pull = NeverPull();
        var identityService = new StubIdentityService(request =>
            new GitRepositoryIdentityResult(
                GitRepositoryIdentityStatus.Succeeded,
                new GitRepositoryIdentity(
                    request.RepositoryPath,
                    new string('a', 40),
                    "main",
                    "origin/main",
                    "origin"),
                "identity loaded"));
        var header = new ProjectHeaderViewModel(identityService);
        using var viewModel = new RepositoryManagerViewModel(resolver, coordinator, pull, header)
        {
            RepositoryUrl = "https://github.com/example/repo.git",
            Branch = "main",
            WorkspaceDirectory = Path.GetTempPath()
        };

        await viewModel.ImportCommand.ExecuteAsync(null);

        Assert.Equal(1, coordinator.CallCount);
        Assert.Equal(repositoryPath, viewModel.RepositoryPath);
        Assert.Equal(backupPath, viewModel.LastBackupPath);
        Assert.Equal("Repository imported successfully.", viewModel.StatusMessage);
        Assert.True(header.HasProject);
        Assert.Equal("repo-ui-success", header.ProjectName);
        Assert.Equal("Branch: main", header.BranchText);
        Assert.Contains(viewModel.Activity, line => line.Contains("Backup preserved", StringComparison.Ordinal));
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.PullCommand.CanExecute(null));
    }

    [Fact]
    public async Task ImportCommand_WhenGitIsUnavailable_DoesNotStartRepositoryMutation()
    {
        var resolver = new StubGitResolver(new GitExecutableResolution(false, null, null, "Git was not found on PATH."));
        var coordinator = new StubImportCoordinator((_, _) =>
            throw new InvalidOperationException("Coordinator must not run without Git."));
        var header = new ProjectHeaderViewModel(new StubIdentityService(_ =>
            throw new InvalidOperationException("Header must not load.")));
        using var viewModel = new RepositoryManagerViewModel(resolver, coordinator, NeverPull(), header)
        {
            RepositoryUrl = "https://github.com/example/repo.git",
            Branch = "main",
            WorkspaceDirectory = Path.GetTempPath()
        };

        await viewModel.ImportCommand.ExecuteAsync(null);

        Assert.Equal(0, coordinator.CallCount);
        Assert.Contains("not found", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(header.HasProject);
        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.PullCommand.CanExecute(null));
    }

    [Fact]
    public async Task ImportCommand_WhenCoordinatorFails_DoesNotClaimProjectLoaded()
    {
        var resolver = new StubGitResolver(new GitExecutableResolution(true, "git.exe", "2.55.0", "Git ready"));
        var coordinator = new StubImportCoordinator((_, _) =>
            new RepositoryImportResult(
                RepositoryImportStatus.ExistingTargetIsNotGit,
                @"C:\workspace\repo",
                Message: "Existing target is not Git."));
        var identity = new StubIdentityService(_ =>
            throw new InvalidOperationException("Identity must not load after failed import."));
        var header = new ProjectHeaderViewModel(identity);
        using var viewModel = new RepositoryManagerViewModel(resolver, coordinator, NeverPull(), header)
        {
            RepositoryUrl = "https://github.com/example/repo.git",
            Branch = "main",
            WorkspaceDirectory = Path.GetTempPath()
        };

        await viewModel.ImportCommand.ExecuteAsync(null);

        Assert.Equal(1, coordinator.CallCount);
        Assert.Equal("Existing target is not Git.", viewModel.StatusMessage);
        Assert.False(header.HasProject);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task PullCommand_FastForwarded_UsesValidatedPullAndRefreshesProjectIdentity()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), "FlutterBuildDoctorTests", "repo-ui-pull");
        var afterSha = new string('b', 40);
        var resolver = new StubGitResolver(new GitExecutableResolution(true, "git.exe", "2.55.0", "Git ready"));
        var coordinator = new StubImportCoordinator((_, _) =>
            throw new InvalidOperationException("Import must not run during pull."));
        var pull = new StubGitPullService((request, progress) =>
        {
            Assert.Equal(repositoryPath, request.RepositoryPath);
            Assert.Equal("git.exe", request.GitExecutablePath);
            progress?.Report(new ProcessOutputLine(DateTimeOffset.UtcNow, ProcessStream.StdOut, "Updating aaaaaaaa..bbbbbbbb"));
            return new GitPullResult(
                GitPullStatus.FastForwarded,
                CurrentBranch: "main",
                Upstream: "origin/main",
                BeforeCommitSha: new string('a', 40),
                AfterCommitSha: afterSha,
                Message: "Fast-forwarded current branch.");
        });
        var identity = new StubIdentityService(request =>
            new GitRepositoryIdentityResult(
                GitRepositoryIdentityStatus.Succeeded,
                new GitRepositoryIdentity(
                    request.RepositoryPath,
                    afterSha,
                    "main",
                    "origin/main",
                    "origin"),
                "identity loaded"));
        var header = new ProjectHeaderViewModel(identity);
        using var viewModel = new RepositoryManagerViewModel(resolver, coordinator, pull, header)
        {
            RepositoryPath = repositoryPath
        };

        Assert.True(viewModel.PullCommand.CanExecute(null));

        await viewModel.PullCommand.ExecuteAsync(null);

        Assert.Equal(1, pull.CallCount);
        Assert.Contains("Updated to bbbbbbbb", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Contains(viewModel.Activity, line => line.Contains("Updating aaaaaaaa..bbbbbbbb", StringComparison.Ordinal));
        Assert.True(header.HasProject);
        Assert.Equal("Commit: bbbbbbbb", header.CommitText);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task PullCommand_UpToDate_ReportsNoChangeAndKeepsRepositoryReady()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), "FlutterBuildDoctorTests", "repo-ui-pull-current");
        var currentSha = new string('c', 40);
        var resolver = new StubGitResolver(new GitExecutableResolution(true, "git.exe", "2.55.0", "Git ready"));
        var pull = new StubGitPullService((_, _) =>
            new GitPullResult(
                GitPullStatus.UpToDate,
                CurrentBranch: "main",
                Upstream: "origin/main",
                BeforeCommitSha: currentSha,
                AfterCommitSha: currentSha,
                Message: "Already up to date."));
        var header = new ProjectHeaderViewModel(new StubIdentityService(request =>
            new GitRepositoryIdentityResult(
                GitRepositoryIdentityStatus.Succeeded,
                new GitRepositoryIdentity(request.RepositoryPath, currentSha, "main", "origin/main", "origin"),
                "identity loaded")));
        using var viewModel = new RepositoryManagerViewModel(
            resolver,
            new StubImportCoordinator((_, _) => throw new InvalidOperationException()),
            pull,
            header)
        {
            RepositoryPath = repositoryPath
        };

        await viewModel.PullCommand.ExecuteAsync(null);

        Assert.Equal(1, pull.CallCount);
        Assert.Contains("already up to date", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Commit: cccccccc", header.CommitText);
        Assert.False(viewModel.IsBusy);
    }

    private static StubGitPullService NeverPull()
        => new((_, _) => throw new InvalidOperationException("Pull must not run in this test."));

    private sealed class StubGitResolver : IGitExecutableResolver
    {
        private readonly GitExecutableResolution _resolution;

        public StubGitResolver(GitExecutableResolution resolution) => _resolution = resolution;

        public Task<GitExecutableResolution> ResolveAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_resolution);
    }

    private sealed class StubImportCoordinator : IRepositoryImportCoordinator
    {
        private readonly Func<RepositoryImportRequest, IProgress<ProcessOutputLine>?, RepositoryImportResult> _handler;

        public StubImportCoordinator(Func<RepositoryImportRequest, IProgress<ProcessOutputLine>?, RepositoryImportResult> handler)
            => _handler = handler;

        public int CallCount { get; private set; }

        public Task<RepositoryImportResult> ImportAsync(
            RepositoryImportRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_handler(request, progress));
        }
    }

    private sealed class StubGitPullService : IGitPullService
    {
        private readonly Func<GitPullRequest, IProgress<ProcessOutputLine>?, GitPullResult> _handler;

        public StubGitPullService(Func<GitPullRequest, IProgress<ProcessOutputLine>?, GitPullResult> handler)
            => _handler = handler;

        public int CallCount { get; private set; }

        public Task<GitPullResult> PullAsync(
            GitPullRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_handler(request, progress));
        }
    }

    private sealed class StubIdentityService : IGitRepositoryIdentityService
    {
        private readonly Func<GitRepositoryIdentityRequest, GitRepositoryIdentityResult> _handler;

        public StubIdentityService(Func<GitRepositoryIdentityRequest, GitRepositoryIdentityResult> handler)
            => _handler = handler;

        public Task<GitRepositoryIdentityResult> ReadAsync(
            GitRepositoryIdentityRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_handler(request));
    }
}
