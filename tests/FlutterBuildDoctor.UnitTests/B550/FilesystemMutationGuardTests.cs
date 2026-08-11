using FlutterBuildDoctor.Application.Filesystem;

namespace FlutterBuildDoctor.UnitTests.B550;

public sealed class FilesystemMutationGuardTests
{
    [Fact]
    public void Evaluate_AllowsSafeMutationAndFingerprintsDeterministically()
    {
        var targets = new[]
        {
            new FilesystemMutationTarget(@"C:\work\app\build\cache.txt", false),
            new FilesystemMutationTarget(@"C:\work\app\tmp", false)
        };
        var first = FilesystemMutationGuard.Evaluate(@"C:\work\app", targets, false, false);
        var second = FilesystemMutationGuard.Evaluate(@"C:\work\app\", targets.AsEnumerable().Reverse(), false, false);

        Assert.True(first.Allowed);
        Assert.False(first.RequiresBackup);
        Assert.False(first.RequiresConfirmation);
        Assert.Equal("safe-mutation", first.ReasonCode);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_RequiresBackupAndConfirmationForDestructiveMutation()
    {
        var target = new[] { new FilesystemMutationTarget(@"C:\work\app\build", true) };
        var noBackup = FilesystemMutationGuard.Evaluate(@"C:\work\app", target, false, false);
        Assert.False(noBackup.Allowed);
        Assert.Equal("backup-required", noBackup.ReasonCode);

        var noConfirmation = FilesystemMutationGuard.Evaluate(@"C:\work\app", target, true, false);
        Assert.False(noConfirmation.Allowed);
        Assert.Equal("confirmation-required", noConfirmation.ReasonCode);

        var approved = FilesystemMutationGuard.Evaluate(@"C:\work\app", target, true, true);
        Assert.True(approved.Allowed);
        Assert.Equal("destructive-approved", approved.ReasonCode);
    }

    [Fact]
    public void Evaluate_RejectsEscapeRootDeletionAndReparseTargets()
    {
        Assert.Throws<ArgumentException>(() => FilesystemMutationGuard.Evaluate(@"C:\work\app", new[] { new FilesystemMutationTarget(@"C:\other\file", false) }, false, false));
        Assert.Throws<ArgumentException>(() => FilesystemMutationGuard.Evaluate(@"C:\work\app", new[] { new FilesystemMutationTarget(@"C:\work\app", true) }, true, true));
        Assert.Throws<ArgumentException>(() => FilesystemMutationGuard.Evaluate(@"C:\work\app", new[] { new FilesystemMutationTarget(@"C:\work\app\link", false, true) }, false, false));
    }

    [Fact]
    public void Evaluate_BoundsMutationBatchAndRejectsRelativeRoot()
    {
        var many = Enumerable.Range(0, FilesystemMutationGuard.MaxTargets + 1)
            .Select(index => new FilesystemMutationTarget($@"C:\work\app\file-{index}.txt", false));
        Assert.Throws<ArgumentOutOfRangeException>(() => FilesystemMutationGuard.Evaluate(@"C:\work\app", many, false, false));
        Assert.Throws<ArgumentException>(() => FilesystemMutationGuard.NormalizeProjectRoot("relative\\root"));
    }
}
