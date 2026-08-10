using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Application.Repairs;

public sealed class StaleBuildDirectoryCleanupRecipe : IRepairRecipe
{
    private readonly IProjectPathGuard _pathGuard;

    public StaleBuildDirectoryCleanupRecipe(IProjectPathGuard pathGuard)
    {
        _pathGuard = pathGuard ?? throw new ArgumentNullException(nameof(pathGuard));
    }

    public string RecipeId => "repair.stale-build-directory";

    public RepairPlan Preview(RepairContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var buildPath = _pathGuard.ResolveProjectChild(context.ProjectRoot, "build");
        var action = new RepairActionPreview(
            "delete-generated-build",
            "Delete the generated Flutter build directory.",
            RepairRisk.Safe,
            new[] { buildPath },
            IsDestructive: true,
            RequiresBackup: false,
            Consequence: "Generated build outputs are removed and must be rebuilt.");
        var safety = RepairSafetyClassifier.Classify(new[] { action });
        return new RepairPlan(
            RecipeId,
            "Clean stale generated build output",
            IssueSignature.Create("FBD.STALE_BUILD", "Flutter build", "stale generated build output"),
            safety.OverallRisk,
            new[] { action },
            safety.RequiresConfirmation,
            RollbackSupported: false,
            new[] { "Verify the project build directory no longer exists." });
    }

    public Task<RepairExecutionResult> ExecuteAsync(
        RepairContext context,
        bool confirmed,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var plan = Preview(context);
        if (!confirmed)
            return Task.FromResult(Rejected(plan));

        cancellationToken.ThrowIfCancellationRequested();
        var buildPath = plan.Actions.Single().AffectedPaths.Single();
        try
        {
            if (Directory.Exists(buildPath))
            {
                var attributes = File.GetAttributes(buildPath);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Refusing to recursively delete a reparse-point build directory.");
                Directory.Delete(buildPath, recursive: true);
            }

            var verified = !Directory.Exists(buildPath);
            var verification = new RepairVerificationResult(
                verified,
                verified ? "Generated build directory removed." : "Generated build directory still exists.",
                new[] { buildPath });
            return Task.FromResult(new RepairExecutionResult(
                plan,
                verified ? RepairExecutionStatus.Succeeded : RepairExecutionStatus.Failed,
                verification,
                Array.Empty<ProcessResult>(),
                FailureReason: verified ? null : "Build directory removal could not be verified."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Task.FromResult(new RepairExecutionResult(
                plan,
                RepairExecutionStatus.Failed,
                new RepairVerificationResult(false, "Generated build cleanup failed verification.", new[] { ex.Message }),
                Array.Empty<ProcessResult>(),
                FailureReason: ex.Message));
        }
    }

    private static RepairExecutionResult Rejected(RepairPlan plan)
        => new(
            plan,
            RepairExecutionStatus.Rejected,
            new RepairVerificationResult(false, "Repair was not executed because confirmation was not granted.", Array.Empty<string>()),
            Array.Empty<ProcessResult>(),
            FailureReason: "Explicit confirmation is required for generated-directory deletion.");
}
