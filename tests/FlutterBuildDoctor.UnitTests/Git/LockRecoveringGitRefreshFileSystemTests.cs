using FlutterBuildDoctor.Git.Repository;

namespace FlutterBuildDoctor.UnitTests.Git;

public sealed class LockRecoveringGitRefreshFileSystemTests
{
    [Fact]
    public void MoveDirectory_WhenFirstMoveIsLocked_ReleasesOwnersAndRetriesSuccessfully()
    {
        var inner = new StubRefreshFileSystem(failuresBeforeSuccess: 1);
        var resolver = new StubWorkspaceLockResolver(
            new WorkspaceLockReleaseResult(
                Supported: true,
                RegisteredFileCount: 3,
                Processes: new[]
                {
                    new WorkspaceLockProcessResult(1234, "dart", Terminated: true)
                },
                Message: "terminated dart"));
        var fileSystem = new LockRecoveringGitRefreshFileSystem(inner, resolver);

        fileSystem.MoveDirectory("C:\\work\\repo", "C:\\work\\repo.backup");

        Assert.Equal(2, inner.MoveCalls);
        Assert.Equal(1, resolver.CallCount);
        Assert.Equal("C:\\work\\repo", resolver.LastRepositoryPath);
    }

    [Fact]
    public void MoveDirectory_WhenLockPersists_ThrowsEvidenceAfterBoundedRetries()
    {
        var inner = new StubRefreshFileSystem(failuresBeforeSuccess: int.MaxValue);
        var resolver = new StubWorkspaceLockResolver(
            new WorkspaceLockReleaseResult(
                Supported: true,
                RegisteredFileCount: 2,
                Processes: new[]
                {
                    new WorkspaceLockProcessResult(
                        4321,
                        "Code",
                        Terminated: false,
                        FailureReason: "access denied")
                },
                Message: "1 owner remains unresolved"));
        var fileSystem = new LockRecoveringGitRefreshFileSystem(inner, resolver);

        var error = Assert.Throws<IOException>(() =>
            fileSystem.MoveDirectory("C:\\work\\repo", "C:\\work\\repo.backup"));

        Assert.Equal(6, inner.MoveCalls);
        Assert.Equal(1, resolver.CallCount);
        Assert.Contains("workspace lock recovery", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 owner remains unresolved", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MoveDirectory_WhenMoveSucceeds_DoesNotInspectOrTerminateAnything()
    {
        var inner = new StubRefreshFileSystem(failuresBeforeSuccess: 0);
        var resolver = new StubWorkspaceLockResolver(
            new WorkspaceLockReleaseResult(
                Supported: true,
                RegisteredFileCount: 0,
                Processes: Array.Empty<WorkspaceLockProcessResult>(),
                Message: "unused"));
        var fileSystem = new LockRecoveringGitRefreshFileSystem(inner, resolver);

        fileSystem.MoveDirectory("C:\\work\\repo", "C:\\work\\repo.backup");

        Assert.Equal(1, inner.MoveCalls);
        Assert.Equal(0, resolver.CallCount);
    }

    private sealed class StubRefreshFileSystem : IGitRefreshFileSystem
    {
        private readonly int _failuresBeforeSuccess;

        public StubRefreshFileSystem(int failuresBeforeSuccess)
        {
            _failuresBeforeSuccess = failuresBeforeSuccess;
        }

        public int MoveCalls { get; private set; }

        public bool DirectoryExists(string path) => false;

        public bool FileExists(string path) => false;

        public string GetFullPath(string path) => path;

        public void MoveDirectory(string sourceDirectoryName, string destinationDirectoryName)
        {
            MoveCalls++;
            if (MoveCalls <= _failuresBeforeSuccess)
            {
                throw new IOException("The process cannot access the file because it is being used by another process.");
            }
        }

        public void DeleteDirectory(string path, bool recursive)
        {
        }
    }

    private sealed class StubWorkspaceLockResolver : IGitWorkspaceLockResolver
    {
        private readonly WorkspaceLockReleaseResult _result;

        public StubWorkspaceLockResolver(WorkspaceLockReleaseResult result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public string? LastRepositoryPath { get; private set; }

        public WorkspaceLockReleaseResult ReleaseLocks(string repositoryPath)
        {
            CallCount++;
            LastRepositoryPath = repositoryPath;
            return _result;
        }
    }
}
