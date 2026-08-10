namespace FlutterBuildDoctor.Git.Repository;

public sealed record GitDirtyRepositorySafetyDecision(
    bool CanReplaceRepository,
    bool RequiresExplicitConfirmation,
    string Message);

public interface IGitDirtyRepositorySafetyPolicy
{
    GitDirtyRepositorySafetyDecision Evaluate(bool isDirty, bool explicitDirtyReplacementApproval);
}

public sealed class GitDirtyRepositorySafetyPolicy : IGitDirtyRepositorySafetyPolicy
{
    public GitDirtyRepositorySafetyDecision Evaluate(bool isDirty, bool explicitDirtyReplacementApproval)
    {
        if (!isDirty)
        {
            return new GitDirtyRepositorySafetyDecision(
                true,
                false,
                "Working tree is clean; repository replacement may proceed after the normal preflight checks.");
        }

        if (!explicitDirtyReplacementApproval)
        {
            return new GitDirtyRepositorySafetyDecision(
                false,
                true,
                "Working tree contains uncommitted changes. Repository replacement is blocked until the user explicitly approves preserving/replacing the dirty repository through the backup workflow.");
        }

        return new GitDirtyRepositorySafetyDecision(
            true,
            true,
            "Dirty repository replacement was explicitly approved; the backup/rollback workflow remains mandatory.");
    }
}
