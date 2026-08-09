using FlutterBuildDoctor.App.ViewModels;
using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Git.Repository;

namespace FlutterBuildDoctor.IntegrationTests.Git;

public sealed class ProjectHeaderViewModelTests
{
    private const string CommitSha = "abcdefabcdefabcdefabcdefabcdefabcdefabcd";

    [Fact]
    public async Task LoadAsync_projects_exact_git_identity_into_header_text()
    {
        var repositoryPath = System.IO.Path.Combine("C:\\work", "sample-app");
        var service = new StubIdentityService(new GitRepositoryIdentityResult(
            GitRepositoryIdentityStatus.Succeeded,
            new GitRepositoryIdentity(
                repositoryPath,
                CommitSha,
                BranchName: "feature/ui",
                Upstream: "origin/feature/ui",
                RemoteName: "origin"),
            "Identity loaded."));
        var viewModel = new ProjectHeaderViewModel(service);

        var result = await viewModel.LoadAsync("git.exe", repositoryPath);

        Assert.True(result.IsSuccess);
        Assert.True(viewModel.HasProject);
        Assert.Equal("sample-app", viewModel.ProjectName);
        Assert.Equal(repositoryPath, viewModel.RepositoryPath);
        Assert.Equal("Branch: feature/ui", viewModel.BranchText);
        Assert.Equal($"Commit: {CommitSha}", viewModel.CommitText);
        Assert.Equal("Remote: origin • Upstream: origin/feature/ui", viewModel.RemoteText);
        Assert.Equal("Identity loaded.", viewModel.IdentityStatus);
        Assert.False(viewModel.IsLoading);
        Assert.Equal("git.exe", service.LastRequest!.GitExecutablePath);
        Assert.Equal(repositoryPath, service.LastRequest.RepositoryPath);
    }

    [Fact]
    public async Task LoadAsync_displays_detached_head_without_inventing_remote_identity()
    {
        var repositoryPath = System.IO.Path.Combine("C:\\work", "detached-app");
        var service = new StubIdentityService(new GitRepositoryIdentityResult(
            GitRepositoryIdentityStatus.Succeeded,
            new GitRepositoryIdentity(
                repositoryPath,
                CommitSha,
                IsDetached: true),
            "Detached HEAD."));
        var viewModel = new ProjectHeaderViewModel(service);

        await viewModel.LoadAsync("git.exe", repositoryPath);

        Assert.Equal("Branch: detached HEAD", viewModel.BranchText);
        Assert.Equal($"Commit: {CommitSha}", viewModel.CommitText);
        Assert.Equal("Remote: —", viewModel.RemoteText);
    }

    [Fact]
    public async Task LoadAsync_surfaces_identity_failure_without_claiming_values()
    {
        var repositoryPath = System.IO.Path.Combine("C:\\work", "broken-app");
        var service = new StubIdentityService(new GitRepositoryIdentityResult(
            GitRepositoryIdentityStatus.Failed,
            Message: "Git identity probe failed."));
        var viewModel = new ProjectHeaderViewModel(service);

        var result = await viewModel.LoadAsync("git.exe", repositoryPath);

        Assert.False(result.IsSuccess);
        Assert.True(viewModel.HasProject);
        Assert.Equal("broken-app", viewModel.ProjectName);
        Assert.Equal("Branch: unavailable", viewModel.BranchText);
        Assert.Equal("Commit: unavailable", viewModel.CommitText);
        Assert.Equal("Remote: unavailable", viewModel.RemoteText);
        Assert.Equal("Git identity probe failed.", viewModel.IdentityStatus);
    }

    [Fact]
    public async Task Clear_restores_no_project_header_after_loaded_identity()
    {
        var repositoryPath = System.IO.Path.Combine("C:\\work", "sample-app");
        var service = new StubIdentityService(new GitRepositoryIdentityResult(
            GitRepositoryIdentityStatus.Succeeded,
            new GitRepositoryIdentity(repositoryPath, CommitSha, BranchName: "main")));
        var viewModel = new ProjectHeaderViewModel(service);
        await viewModel.LoadAsync("git.exe", repositoryPath);

        viewModel.Clear();

        Assert.False(viewModel.HasProject);
        Assert.Equal("No project selected", viewModel.ProjectName);
        Assert.Equal("Branch: —", viewModel.BranchText);
        Assert.Equal("Commit: —", viewModel.CommitText);
        Assert.Equal("Remote: —", viewModel.RemoteText);
    }

    private sealed class StubIdentityService : IGitRepositoryIdentityService
    {
        private readonly GitRepositoryIdentityResult _result;

        public StubIdentityService(GitRepositoryIdentityResult result)
        {
            _result = result;
        }

        public GitRepositoryIdentityRequest? LastRequest { get; private set; }

        public Task<GitRepositoryIdentityResult> ReadAsync(
            GitRepositoryIdentityRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = request;
            return Task.FromResult(_result);
        }
    }
}
