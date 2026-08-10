using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Application.Repairs;
using FlutterBuildDoctor.Flutter.Commands;

namespace FlutterBuildDoctor.Flutter.Repairs;

public sealed class FlutterCleanRepairRecipe : IRepairRecipe
{
    private readonly IFlutterCommandService _commands;
    private readonly IRepairVerifier _verifier;

    public FlutterCleanRepairRecipe(IFlutterCommandService commands, IRepairVerifier verifier)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
    }

    public string RecipeId => "repair.flutter-clean";

    public RepairPlan Preview(RepairContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var action = new RepairActionPreview(
            "flutter-clean",
            "Run flutter clean in the selected project.",
            RepairRisk.Safe,
            new[] { context.ProjectRoot },
            IsDestructive: true,
            RequiresBackup: false,
            Consequence: "Generated Flutter/Android build state is removed and must be regenerated.");
        var safety = RepairSafetyClassifier.Classify(new[] { action });
        return new RepairPlan(
            RecipeId,
            "Flutter clean",
            IssueSignature.Create("FBD.FLUTTER_CLEAN", "Flutter build", "generated flutter build state requires cleanup"),
            safety.OverallRisk,
            new[] { action },
            safety.RequiresConfirmation,
            RollbackSupported: false,
            new[] { "Require flutter clean to exit successfully." });
    }

    public async Task<RepairExecutionResult> ExecuteAsync(
        RepairContext context,
        bool confirmed,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var plan = Preview(context);
        if (!confirmed) return Rejected(plan);
        var execution = await _commands.CleanAsync(
            new FlutterCommandContext(context.FlutterExecutable, context.ProjectRoot),
            progress,
            cancellationToken).ConfigureAwait(false);
        return FromExecution(plan, execution.ProcessResult, _verifier, "flutter clean completed successfully.");
    }

    private static RepairExecutionResult Rejected(RepairPlan plan)
        => RepairRecipeResult.Rejected(plan);

    internal static RepairExecutionResult FromExecution(
        RepairPlan plan,
        ProcessResult result,
        IRepairVerifier verifier,
        string successSummary)
    {
        var verification = verifier.VerifyProcessResults(new[] { result }, successSummary, "Repair command did not complete successfully.");
        var status = result.Status == ProcessExecutionStatus.Cancelled
            ? RepairExecutionStatus.Cancelled
            : verification.IsVerified ? RepairExecutionStatus.Succeeded : RepairExecutionStatus.Failed;
        return new RepairExecutionResult(
            plan,
            status,
            verification,
            new[] { result },
            FailureReason: verification.IsVerified ? null : result.FailureReason);
    }
}

public sealed class DependencyRefreshRepairRecipe : IRepairRecipe
{
    private readonly IFlutterCommandService _commands;
    private readonly IRepairVerifier _verifier;

    public DependencyRefreshRepairRecipe(IFlutterCommandService commands, IRepairVerifier verifier)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
    }

    public string RecipeId => "repair.flutter-pub-get";

    public RepairPlan Preview(RepairContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var action = new RepairActionPreview(
            "flutter-pub-get",
            "Resolve project dependencies with flutter pub get.",
            RepairRisk.Risky,
            new[] { context.ProjectRoot },
            IsDestructive: false,
            RequiresBackup: false,
            Consequence: "Dependency resolution metadata and the lock file may change when constraints permit it.");
        var safety = RepairSafetyClassifier.Classify(new[] { action });
        return new RepairPlan(
            RecipeId,
            "Refresh Flutter dependencies",
            IssueSignature.Create("FBD.PUB_REFRESH", "Flutter dependencies", "dependency metadata requires refresh"),
            safety.OverallRisk,
            new[] { action },
            safety.RequiresConfirmation,
            RollbackSupported: false,
            new[] { "Require flutter pub get to exit successfully." });
    }

    public async Task<RepairExecutionResult> ExecuteAsync(
        RepairContext context,
        bool confirmed,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var plan = Preview(context);
        if (!confirmed) return RepairRecipeResult.Rejected(plan);
        var execution = await _commands.PubGetAsync(
            new FlutterCommandContext(context.FlutterExecutable, context.ProjectRoot),
            progress,
            cancellationToken).ConfigureAwait(false);
        return FlutterCleanRepairRecipe.FromExecution(
            plan,
            execution.ProcessResult,
            _verifier,
            "flutter pub get completed successfully.");
    }
}

internal static class RepairRecipeResult
{
    public static RepairExecutionResult Rejected(RepairPlan plan)
        => new(
            plan,
            RepairExecutionStatus.Rejected,
            new RepairVerificationResult(false, "Repair was not executed because confirmation was not granted.", Array.Empty<string>()),
            Array.Empty<ProcessResult>(),
            FailureReason: "Explicit confirmation is required.");
}
