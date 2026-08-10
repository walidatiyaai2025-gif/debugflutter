using FlutterBuildDoctor.Git.Repository;

namespace FlutterBuildDoctor.UnitTests.QA;

public sealed class GitDirtyRepositorySafetyPolicyTests
{
    private readonly GitDirtyRepositorySafetyPolicy _policy = new();

    [Fact]
    public void DirtyRepository_IsBlockedWithoutExplicitApproval()
    {
        var decision = _policy.Evaluate(isDirty: true, explicitDirtyReplacementApproval: false);

        Assert.False(decision.CanReplaceRepository);
        Assert.True(decision.RequiresExplicitConfirmation);
        Assert.Contains("uncommitted", decision.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DirtyRepository_ApprovalStillRequiresBackupWorkflow()
    {
        var decision = _policy.Evaluate(isDirty: true, explicitDirtyReplacementApproval: true);

        Assert.True(decision.CanReplaceRepository);
        Assert.True(decision.RequiresExplicitConfirmation);
        Assert.Contains("backup", decision.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CleanRepository_DoesNotRequireDirtyTreeConfirmation()
    {
        var decision = _policy.Evaluate(isDirty: false, explicitDirtyReplacementApproval: false);

        Assert.True(decision.CanReplaceRepository);
        Assert.False(decision.RequiresExplicitConfirmation);
    }
}
