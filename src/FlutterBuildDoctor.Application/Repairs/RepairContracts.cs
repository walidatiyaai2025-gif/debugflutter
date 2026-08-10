using System.Security.Cryptography;
using System.Text;
using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Application.Repairs;

public enum RepairRisk
{
    Safe = 0,
    Risky,
    Destructive
}

public enum RepairExecutionStatus
{
    Rejected = 0,
    Succeeded,
    Failed,
    Cancelled
}

public sealed record IssueSignature(
    string Code,
    string Category,
    string NormalizedEvidence,
    string StableKey)
{
    public static IssueSignature Create(string code, string category, string evidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentNullException.ThrowIfNull(evidence);
        var normalized = NormalizeEvidence(evidence);
        var keyInput = $"{code.Trim().ToUpperInvariant()}|{category.Trim().ToUpperInvariant()}|{normalized}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(keyInput));
        return new IssueSignature(
            code.Trim(),
            category.Trim(),
            normalized,
            Convert.ToHexString(hash).ToLowerInvariant());
    }

    private static string NormalizeEvidence(string evidence)
        => string.Join(
            ' ',
            evidence
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToLowerInvariant();
}

public sealed record RepairActionPreview(
    string ActionId,
    string Description,
    RepairRisk Risk,
    IReadOnlyList<string> AffectedPaths,
    bool IsDestructive,
    bool RequiresBackup,
    string? Consequence = null);

public sealed record RepairPlan(
    string RecipeId,
    string Title,
    IssueSignature Signature,
    RepairRisk Risk,
    IReadOnlyList<RepairActionPreview> Actions,
    bool RequiresConfirmation,
    bool RollbackSupported,
    IReadOnlyList<string> VerificationSteps);

public sealed record RepairContext(
    string ProjectRoot,
    string FlutterExecutable = "flutter",
    string AdbExecutable = "adb");

public sealed record RepairVerificationResult(
    bool IsVerified,
    string Summary,
    IReadOnlyList<string> Evidence);

public sealed record RepairRestoreEntry(
    string OriginalPath,
    string BackupPath,
    bool IsDirectory);

public sealed record RepairRestorePoint(
    Guid RestorePointId,
    string ProjectRoot,
    string BackupRoot,
    DateTimeOffset CreatedAt,
    IReadOnlyList<RepairRestoreEntry> Entries);

public sealed record RepairExecutionResult(
    RepairPlan Plan,
    RepairExecutionStatus Status,
    RepairVerificationResult Verification,
    IReadOnlyList<ProcessResult> ProcessResults,
    RepairRestorePoint? RestorePoint = null,
    string? FailureReason = null)
{
    public bool IsSuccess => Status == RepairExecutionStatus.Succeeded && Verification.IsVerified;
}

public interface IRepairRecipe
{
    string RecipeId { get; }
    RepairPlan Preview(RepairContext context);
    Task<RepairExecutionResult> ExecuteAsync(
        RepairContext context,
        bool confirmed,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IRepairBackupService
{
    Task<RepairRestorePoint> CreateAsync(
        string projectRoot,
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default);

    Task RollbackAsync(
        RepairRestorePoint restorePoint,
        bool confirmed,
        CancellationToken cancellationToken = default);
}

public interface IRepairVerifier
{
    RepairVerificationResult VerifyProcessResults(
        IReadOnlyCollection<ProcessResult> results,
        string successSummary,
        string failureSummary);
}

public interface IProjectPathGuard
{
    string ResolveProjectChild(string projectRoot, string relativePath);
}
