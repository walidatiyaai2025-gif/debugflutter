using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Application.Repairs;

namespace FlutterBuildDoctor.Android.Repairs;

public sealed class AdbRestartRepairRecipe : IRepairRecipe
{
    private readonly IProcessRunner _processRunner;

    public AdbRestartRepairRecipe(IProcessRunner processRunner)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public string RecipeId => "repair.adb-restart";

    public RepairPlan Preview(RepairContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var action = new RepairActionPreview(
            "adb-restart",
            "Restart the ADB server and verify it responds to adb devices.",
            RepairRisk.Risky,
            Array.Empty<string>(),
            IsDestructive: false,
            RequiresBackup: false,
            Consequence: "Active ADB/debug sessions may be interrupted and reconnect.");
        var safety = RepairSafetyClassifier.Classify(new[] { action });
        return new RepairPlan(
            RecipeId,
            "Restart ADB server",
            IssueSignature.Create("FBD.ADB_RESTART", "Android device bridge", "adb server state requires restart"),
            safety.OverallRisk,
            new[] { action },
            safety.RequiresConfirmation,
            RollbackSupported: false,
            new[] { "Start the ADB server.", "Verify adb devices responds successfully." });
    }

    public async Task<RepairExecutionResult> ExecuteAsync(
        RepairContext context,
        bool confirmed,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var plan = Preview(context);
        if (!confirmed)
        {
            return new RepairExecutionResult(
                plan,
                RepairExecutionStatus.Rejected,
                new RepairVerificationResult(false, "ADB restart was not executed because confirmation was not granted.", Array.Empty<string>()),
                Array.Empty<ProcessResult>(),
                FailureReason: "Explicit confirmation is required because ADB sessions may be interrupted.");
        }

        var results = new List<ProcessResult>();
        results.Add(await RunAsync(context, new[] { "kill-server" }, "adb kill-server", progress, cancellationToken).ConfigureAwait(false));
        results.Add(await RunAsync(context, new[] { "start-server" }, "adb start-server", progress, cancellationToken).ConfigureAwait(false));
        if (results[^1].Status == ProcessExecutionStatus.Cancelled)
        {
            return Cancelled(plan, results);
        }

        results.Add(await RunAsync(context, new[] { "devices" }, "adb devices verification", progress, cancellationToken).ConfigureAwait(false));
        var verified = results[1].IsSuccess && results[2].IsSuccess;
        var verification = new RepairVerificationResult(
            verified,
            verified ? "ADB server restarted and responded to verification." : "ADB restart could not be verified.",
            results.Select(result => $"{result.SanitizedCommand}: {result.Status}").ToArray());
        return new RepairExecutionResult(
            plan,
            verified ? RepairExecutionStatus.Succeeded : RepairExecutionStatus.Failed,
            verification,
            results,
            FailureReason: verified ? null : results.LastOrDefault(result => !result.IsSuccess)?.FailureReason);
    }

    private Task<ProcessResult> RunAsync(
        RepairContext context,
        IReadOnlyList<string> arguments,
        string displayName,
        IProgress<ProcessOutputLine>? progress,
        CancellationToken cancellationToken)
        => _processRunner.RunAsync(
            new ProcessRequest(
                context.AdbExecutable,
                arguments,
                context.ProjectRoot,
                Timeout: TimeSpan.FromSeconds(30),
                DisplayName: displayName),
            progress,
            cancellationToken);

    private static RepairExecutionResult Cancelled(RepairPlan plan, IReadOnlyList<ProcessResult> results)
        => new(
            plan,
            RepairExecutionStatus.Cancelled,
            new RepairVerificationResult(false, "ADB restart was cancelled.", results.Select(result => result.Status.ToString()).ToArray()),
            results,
            FailureReason: "ADB restart cancelled.");
}
