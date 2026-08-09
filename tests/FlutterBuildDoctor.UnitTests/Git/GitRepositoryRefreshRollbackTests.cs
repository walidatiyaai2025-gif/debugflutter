using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Git.Cloning;
using FlutterBuildDoctor.Git.Repository;

namespace FlutterBuildDoctor.UnitTests.Git;

public sealed class GitRepositoryRefreshRollbackTests
{
    [Fact]
    public async Task RefreshAsync_WhenReplacementMoveFails_RestoresBackupWithoutLosingOriginalContent()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "FlutterBuildDoctorTests",
            Guid.NewGuid().ToString("N"));
        var repositoryPath = Path.Combine(root, "repo");

        try
        {
            Directory.CreateDirectory(Path.Combine(repositoryPath, ".git"));
            File.WriteAllText(Path.Combine(repositoryPath, "original.txt"), "original");

            var scanner = new CleanScanner();
            var clone = new CreatingCloneService();
            var identity = new SuccessIdentityService();
            var fileSystem = new FailStagingPromotionFileSystem();
            var service = new GitRepositoryRefreshService(clone, scanner, identity, fileSystem);

            var result = await service.RefreshAsync(
                new GitRepositoryRefreshRequest(
                    "git.exe",
                    "https://github.com/example/repo.git",
                    repositoryPath,
                    TimeSpan.FromSeconds(30)));

            Assert.Equal(GitRepositoryRefreshStatus.SwapFailed, result.Status);
            Assert.True(result.RollbackPerformed);
            Assert.True(File.Exists(Path.Combine(repositoryPath, "original.txt")));
            Assert.False(File.Exists(Path.Combine(repositoryPath, "replacement.txt")));
            Assert.True(Directory.Exists(Path.Combine(repositoryPath, ".git")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class CleanScanner : IGitWorkingTreeScanner
    {
        public Task<GitWorkingTreeScanResult> ScanAsync(
            GitWorkingTreeScanRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new GitWorkingTreeScanResult(
                GitWorkingTreeScanStatus.Succeeded,
                Array.Empty<GitWorkingTreeChange>()));
    }

    private sealed class CreatingCloneService : IGitCloneService
    {
        public Task<GitCloneResult> CloneAsync(
            GitCloneRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var path = Path.Combine(request.WorkspaceDirectory, request.TargetDirectoryName!);
            Directory.CreateDirectory(Path.Combine(path, ".git"));
            File.WriteAllText(Path.Combine(path, "replacement.txt"), "replacement");
            return Task.FromResult(new GitCloneResult(
                GitCloneStatus.Succeeded,
                path,
                "cloned"));
        }
    }

    private sealed class SuccessIdentityService : IGitRepositoryIdentityService
    {
        public Task<GitRepositoryIdentityResult> ReadAsync(
            GitRepositoryIdentityRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new GitRepositoryIdentityResult(
                GitRepositoryIdentityStatus.Succeeded,
                new GitRepositoryIdentity(
                    request.RepositoryPath,
                    new string('a', 40),
                    "main",
                    "origin/main",
                    "origin"),
                "identity ok"));
    }

    private sealed class FailStagingPromotionFileSystem : IGitRefreshFileSystem
    {
        public bool DirectoryExists(string path)
            => Directory.Exists(path);

        public bool FileExists(string path)
            => File.Exists(path);

        public string GetFullPath(string path)
            => Path.GetFullPath(path);

        public void MoveDirectory(string sourceDirectoryName, string destinationDirectoryName)
        {
            if (Path.GetFileName(sourceDirectoryName).Contains(".fbd-staging-", StringComparison.Ordinal))
            {
                throw new IOException("simulated staging promotion failure");
            }

            Directory.Move(sourceDirectoryName, destinationDirectoryName);
        }

        public void DeleteDirectory(string path, bool recursive)
            => Directory.Delete(path, recursive);
    }
}
