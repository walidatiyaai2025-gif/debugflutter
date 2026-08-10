namespace FlutterBuildDoctor.Application.Repairs;

public sealed record RepairSafetyAssessment(
    RepairRisk OverallRisk,
    bool RequiresConfirmation,
    bool RequiresBackup,
    bool ContainsDestructiveAction);

public static class RepairSafetyClassifier
{
    public static RepairSafetyAssessment Classify(IReadOnlyCollection<RepairActionPreview> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        var risk = actions.Count == 0 ? RepairRisk.Safe : actions.Max(static action => action.Risk);
        var destructive = actions.Any(static action => action.IsDestructive);
        var backup = actions.Any(static action => action.RequiresBackup);
        return new RepairSafetyAssessment(
            risk,
            destructive || risk != RepairRisk.Safe,
            backup,
            destructive);
    }
}

public sealed class RepairVerifier : IRepairVerifier
{
    public RepairVerificationResult VerifyProcessResults(
        IReadOnlyCollection<FlutterBuildDoctor.Application.Processes.ProcessResult> results,
        string successSummary,
        string failureSummary)
    {
        ArgumentNullException.ThrowIfNull(results);
        var failures = results.Where(static result => !result.IsSuccess).ToArray();
        if (failures.Length == 0)
        {
            return new RepairVerificationResult(
                true,
                successSummary,
                results.Select(static result => $"{result.SanitizedCommand}: {result.Status}").ToArray());
        }

        return new RepairVerificationResult(
            false,
            failureSummary,
            failures.Select(static result =>
                $"{result.SanitizedCommand}: {result.Status} — {result.FailureReason ?? "no failure reason"}").ToArray());
    }
}

public sealed class ProjectPathGuard : IProjectPathGuard
{
    public string ResolveProjectChild(string projectRoot, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath))
            throw new ArgumentException("Project child path must be relative.", nameof(relativePath));

        var root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        var prefix = root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Resolved path escapes the selected project root.");
        if (string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Project root itself cannot be used as a repair child path.");
        return candidate;
    }
}
