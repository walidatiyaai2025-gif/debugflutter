using FlutterBuildDoctor.App.Services;
using FlutterBuildDoctor.App.ViewModels;
using FlutterBuildDoctor.Application.Processes;
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
        var coordinator = new StubImportCoordinator((_, _) =>
            new RepositoryImportResult(
                RepositoryImportStatus.Succeeded,
                repositoryPath,
                backupPath,
                "Repository imported successfully."));
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
        using var viewModel = new RepositoryManagerViewModel(resolver, coordinator, header)
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
    }

    [Fact]
    public async Task ImportCommand_WhenGitIsUnavailable_DoesNotStartRepositoryMutation()
    {
        var resolver = new StubGitResolver(new GitExecutableResolution(false, null, null, "Git was not found on PATH."));
        var coordinator = new StubImportCoordinator((_, _) =>
            throw new InvalidOperationException("Coordinator must not run without Git."));
        var header = new ProjectHeaderViewModel(new StubIdentityService(_ =>
            throw new InvalidOperationException("Header must not load.")));
        using var viewModel = new RepositoryManagerViewModel(resolver, coordinator, header)
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
        using var viewModel = new RepositoryManagerViewModel(resolver, coordinator, header)
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
