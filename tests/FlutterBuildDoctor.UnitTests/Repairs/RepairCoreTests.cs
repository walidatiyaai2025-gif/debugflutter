using FlutterBuildDoctor.Application.Repairs;
using FlutterBuildDoctor.Infrastructure.Repairs;

namespace FlutterBuildDoctor.UnitTests.Repairs;

public sealed class RepairCoreTests
{
    [Fact]
    public void IssueSignature_IsStableAcrossWhitespaceAndCaseNoise()
    {
        var first = IssueSignature.Create("gradle.daemon", "Gradle", "Daemon   DISAPPEARED\r\nunexpectedly");
        var second = IssueSignature.Create("GRADLE.DAEMON", "gradle", " daemon disappeared unexpectedly ");

        Assert.Equal(first.StableKey, second.StableKey);
        Assert.Equal("daemon disappeared unexpectedly", first.NormalizedEvidence);
    }

    [Fact]
    public void SafetyClassifier_RequiresConfirmationForDestructiveSafeAction()
    {
        var assessment = RepairSafetyClassifier.Classify(new[]
        {
            new RepairActionPreview("cleanup", "Cleanup generated output", RepairRisk.Safe, Array.Empty<string>(), true, false)
        });

        Assert.Equal(RepairRisk.Safe, assessment.OverallRisk);
        Assert.True(assessment.RequiresConfirmation);
        Assert.True(assessment.ContainsDestructiveAction);
    }

    [Fact]
    public async Task BackupAndRollback_RestoreOriginalFileOnlyAfterExplicitConfirmation()
    {
        var root = Path.Combine(Path.GetTempPath(), $"fbd-repair-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "settings.gradle");
        await File.WriteAllTextAsync(file, "before");
        try
        {
            var service = new FileSystemRepairBackupService();
            var restorePoint = await service.CreateAsync(root, new[] { "settings.gradle" });
            await File.WriteAllTextAsync(file, "after");

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RollbackAsync(restorePoint, confirmed: false));
            Assert.Equal("after", await File.ReadAllTextAsync(file));

            await service.RollbackAsync(restorePoint, confirmed: true);
            Assert.Equal("before", await File.ReadAllTextAsync(file));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ProjectPathGuard_RejectsTraversalOutsideProject()
    {
        var root = Path.Combine(Path.GetTempPath(), $"fbd-path-{Guid.NewGuid():N}");
        var guard = new ProjectPathGuard();

        Assert.EndsWith(Path.Combine(Path.GetFileName(root), "build"), guard.ResolveProjectChild(root, "build"), StringComparison.OrdinalIgnoreCase);
        Assert.Throws<InvalidOperationException>(() => guard.ResolveProjectChild(root, Path.Combine("..", "outside")));
    }

    [Fact]
    public async Task StaleBuildCleanup_RequiresConfirmationAndDeletesOnlyBuildDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"fbd-cleanup-{Guid.NewGuid():N}");
        var build = Path.Combine(root, "build");
        var keep = Path.Combine(root, "lib");
        Directory.CreateDirectory(build);
        Directory.CreateDirectory(keep);
        await File.WriteAllTextAsync(Path.Combine(build, "old.bin"), "old");
        await File.WriteAllTextAsync(Path.Combine(keep, "main.dart"), "keep");
        try
        {
            var recipe = new StaleBuildDirectoryCleanupRecipe(new ProjectPathGuard());
            var context = new RepairContext(root);

            var rejected = await recipe.ExecuteAsync(context, confirmed: false);
            Assert.Equal(RepairExecutionStatus.Rejected, rejected.Status);
            Assert.True(Directory.Exists(build));

            var completed = await recipe.ExecuteAsync(context, confirmed: true);
            Assert.True(completed.IsSuccess);
            Assert.False(Directory.Exists(build));
            Assert.True(File.Exists(Path.Combine(keep, "main.dart")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
