using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Git.Cloning;
using FlutterBuildDoctor.Git.Repository;

namespace FlutterBuildDoctor.UnitTests.Git;

public sealed class GitRepositoryRefreshServiceTests
{
    [Fact]
    public async Task RefreshAsync_PreservesDirtyOriginalInBackupAndPromotesVerifiedClone()
    {
        using var fixture = new RefreshFixture();
        fixture.CreateOriginalRepository("local-change.txt");

        var scanner = new StubWorkingTreeScanner(
            new GitWorkingTreeScanResult(
                GitWorkingTreeScanStatus.Succeeded,
                new[]
                {
                    new GitWorkingTreeChange(
                        "local-change.txt",
                        GitWorkingTreeChangeKind.Modified,
                        " M",
                        IsStaged: false,
                        IsUnstaged: true)
                }));
        var clone = new StubCloneService(request =>
        {
            var path = Path.Combine(request.WorkspaceDirectory, request.TargetDirectoryName!);
            Directory.CreateDirectory(Path.Combine(path, ".git"));
            File.WriteAllText(Path.Combine(path, "fresh.txt"), "fresh");
            return new GitCloneResult(GitCloneStatus.Succeeded, path, "cloned");
        });
        var identity = new StubIdentityService((request, _) => SuccessIdentity(request.RepositoryPath));
        var service = new GitRepositoryRefreshService(clone, scanner, identity);

        var result = await service.RefreshAsync(fixture.Request);

        Assert.True(result.IsSuccess);
        Assert.True(result.OriginalWasDirty);
        Assert.NotNull(result.BackupPath);
        Assert.True(Directory.Exists(result.BackupPath));
        Assert.True(File.Exists(Path.Combine(result.BackupPath!, "local-change.txt")));
        Assert.False(File.Exists(Path.Combine(fixture.RepositoryPath, "local-change.txt")));
        Assert.True(File.Exists(Path.Combine(fixture.RepositoryPath, "fresh.txt")));
        Assert.True(Directory.Exists(Path.Combine(fixture.RepositoryPath, ".git")));
        Assert.False(result.RollbackPerformed);
        Assert.Equal(3, identity.CallCount);
    }

    [Fact]
    public async Task RefreshAsync_WhenCloneFails_LeavesOriginalUntouched()
    {
        using var fixture = new RefreshFixture();
        fixture.CreateOriginalRepository("keep.txt");

        var scanner = CleanScanner();
        var clone = new StubCloneService(_ =>
            new GitCloneResult(GitCloneStatus.Failed, null, "network failed"));
        var identity = new StubIdentityService((request, _) => SuccessIdentity(request.RepositoryPath));
        var service = new GitRepositoryRefreshService(clone, scanner, identity);

        var result = await service.RefreshAsync(fixture.Request);

        Assert.Equal(GitRepositoryRefreshStatus.CloneFailed, result.Status);
        Assert.True(File.Exists(Path.Combine(fixture.RepositoryPath, "keep.txt")));
        Assert.True(Directory.Exists(Path.Combine(fixture.RepositoryPath, ".git")));
        Assert.Null(result.BackupPath);
        Assert.False(result.RollbackPerformed);
        Assert.Equal(1, identity.CallCount);
    }

    [Fact]
    public async Task RefreshAsync_WhenPromotedCloneFailsVerification_RestoresOriginalAndQuarantinesReplacement()
    {
        using var fixture = new RefreshFixture();
        fixture.CreateOriginalRepository("original.txt");

        var scanner = CleanScanner();
        var clone = new StubCloneService(request =>
        {
            var path = Path.Combine(request.WorkspaceDirectory, request.TargetDirectoryName!);
            Directory.CreateDirectory(Path.Combine(path, ".git"));
            File.WriteAllText(Path.Combine(path, "replacement.txt"), "replacement");
            return new GitCloneResult(GitCloneStatus.Succeeded, path, "cloned");
        });
        var identity = new StubIdentityService((request, call) =>
            call < 3
                ? SuccessIdentity(request.RepositoryPath)
                : new GitRepositoryIdentityResult(
                    GitRepositoryIdentityStatus.VerificationFailed,
                    Message: "final verification failed"));
        var service = new GitRepositoryRefreshService(clone, scanner, identity);

        var result = await service.RefreshAsync(fixture.Request);

        Assert.Equal(GitRepositoryRefreshStatus.VerificationFailed, result.Status);
        Assert.True(result.RollbackPerformed);
        Assert.True(File.Exists(Path.Combine(fixture.RepositoryPath, "original.txt")));
        Assert.False(File.Exists(Path.Combine(fixture.RepositoryPath, "replacement.txt")));
        Assert.NotNull(result.FailedReplacementPath);
        Assert.True(File.Exists(Path.Combine(result.FailedReplacementPath!, "replacement.txt")));
        Assert.Equal(3, identity.CallCount);
    }

    [Fact]
    public async Task RefreshAsync_WhenWorkingTreePreflightFails_DoesNotCloneOrMoveRepository()
    {
        using var fixture = new RefreshFixture();
        fixture.CreateOriginalRepository("original.txt");

        var scanner = new StubWorkingTreeScanner(
            new GitWorkingTreeScanResult(
                GitWorkingTreeScanStatus.ParseFailed,
                Array.Empty<GitWorkingTreeChange>(),
                Message: "malformed porcelain",
                RawStatus: "raw"));
        var clone = new StubCloneService(_ =>
            throw new InvalidOperationException("Clone must not run after failed preflight."));
        var identity = new StubIdentityService((request, _) => SuccessIdentity(request.RepositoryPath));
        var service = new GitRepositoryRefreshService(clone, scanner, identity);

        var result = await service.RefreshAsync(fixture.Request);

        Assert.Equal(GitRepositoryRefreshStatus.PreflightFailed, result.Status);
        Assert.True(File.Exists(Path.Combine(fixture.RepositoryPath, "original.txt")));
        Assert.Equal(0, clone.CallCount);
        Assert.Equal(0, identity.CallCount);
    }

    [Fact]
    public async Task RefreshAsync_RejectsExistingNonGitDirectory()
    {
        using var fixture = new RefreshFixture();
        Directory.CreateDirectory(fixture.RepositoryPath);
        File.WriteAllText(Path.Combine(fixture.RepositoryPath, "data.txt"), "keep");

        var service = new GitRepositoryRefreshService(
            new StubCloneService(_ => throw new InvalidOperationException()),
            CleanScanner(),
            new StubIdentityService((request, _) => SuccessIdentity(request.RepositoryPath)));

        var result = await service.RefreshAsync(fixture.Request);

        Assert.Equal(GitRepositoryRefreshStatus.InvalidRepository, result.Status);
        Assert.True(File.Exists(Path.Combine(fixture.RepositoryPath, "data.txt")));
    }

    private static StubWorkingTreeScanner CleanScanner()
        => new(new GitWorkingTreeScanResult(
            GitWorkingTreeScanStatus.Succeeded,
            Array.Empty<GitWorkingTreeChange>()));

    private static GitRepositoryIdentityResult SuccessIdentity(string path)
        => new(
            GitRepositoryIdentityStatus.Succeeded,
            new GitRepositoryIdentity(path, new string('a', 40), "main", "origin/main", "origin"),
            "identity ok");

    private sealed class RefreshFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "FlutterBuildDoctorTests",
            Guid.NewGuid().ToString("N"));

        public RefreshFixture()
        {
            Directory.CreateDirectory(_root);
            RepositoryPath = Path.Combine(_root, "repo");
            Request = new GitRepositoryRefreshRequest(
                "git.exe",
                "https://github.com/example/repo.git",
                RepositoryPath,
                TimeSpan.FromSeconds(30));
        }

        public string RepositoryPath { get; }

        public GitRepositoryRefreshRequest Request { get; }

        public void CreateOriginalRepository(string fileName)
        {
            Directory.CreateDirectory(Path.Combine(RepositoryPath, ".git"));
            File.WriteAllText(Path.Combine(RepositoryPath, fileName), "original");
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }

    private sealed class StubWorkingTreeScanner : IGitWorkingTreeScanner
    {
        private readonly GitWorkingTreeScanResult _result;

        public StubWorkingTreeScanner(GitWorkingTreeScanResult result)
        {
            _result = result;
        }

        public Task<GitWorkingTreeScanResult> ScanAsync(
            GitWorkingTreeScanRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
    }

    private sealed class StubCloneService : IGitCloneService
    {
        private readonly Func<GitCloneRequest, GitCloneResult> _handler;

        public StubCloneService(Func<GitCloneRequest, GitCloneResult> handler)
        {
            _handler = handler;
        }

        public int CallCount { get; private set; }

        public Task<GitCloneResult> CloneAsync(
            GitCloneRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_handler(request));
        }
    }

    private sealed class StubIdentityService : IGitRepositoryIdentityService
    {
        private readonly Func<GitRepositoryIdentityRequest, int, GitRepositoryIdentityResult> _handler;

        public StubIdentityService(Func<GitRepositoryIdentityRequest, int, GitRepositoryIdentityResult> handler)
        {
            _handler = handler;
        }

        public int CallCount { get; private set; }

        public Task<GitRepositoryIdentityResult> ReadAsync(
            GitRepositoryIdentityRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_handler(request, CallCount));
        }
    }
}
