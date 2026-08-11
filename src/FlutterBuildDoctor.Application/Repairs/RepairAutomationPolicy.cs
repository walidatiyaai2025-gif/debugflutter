namespace FlutterBuildDoctor.Application.Repairs;

public enum AutomationRepairRisk
{
    Unknown = 0,
    Safe = 1,
    Risky = 2,
    Destructive = 3
}

public sealed record RepairAutomationDecision(
    string RecipeId,
    AutomationRepairRisk Risk,
    bool KnownRecipe,
    bool WithinProjectRoot,
    bool RequiresConfirmation,
    bool RequiresBackup,
    bool RequiresVerification,
    bool RollbackAvailable,
    bool CanAutoRun,
    string ReasonCode);

public static class RepairAutomationPolicy
{
    private static readonly IReadOnlyDictionary<string, AutomationRepairRisk> Recipes =
        new Dictionary<string, AutomationRepairRisk>(StringComparer.OrdinalIgnoreCase)
        {
            ["repair.flutter-clean"] = AutomationRepairRisk.Safe,
            ["repair.pub-get"] = AutomationRepairRisk.Safe,
            ["repair.adb-restart"] = AutomationRepairRisk.Safe,
            ["repair.gradle-cache"] = AutomationRepairRisk.Risky,
            ["repair.generated-clean"] = AutomationRepairRisk.Destructive
        };

    public static RepairAutomationDecision Decide(
        string recipeId,
        string projectRoot,
        string targetPath,
        bool hasBackupEvidence = false)
    {
        var normalizedRecipe = NormalizeRecipeId(recipeId);
        var known = Recipes.TryGetValue(normalizedRecipe, out var risk);
        if (!known) risk = AutomationRepairRisk.Unknown;

        var withinRoot = IsWithinProjectRoot(projectRoot, targetPath);
        var requiresConfirmation = risk is AutomationRepairRisk.Risky or AutomationRepairRisk.Destructive;
        var requiresBackup = risk == AutomationRepairRisk.Destructive;
        var rollbackAvailable = hasBackupEvidence;
        var canAutoRun = known && withinRoot && risk == AutomationRepairRisk.Safe;

        var reason = !known
            ? "unknown_recipe"
            : !withinRoot
                ? "path_escape"
                : risk == AutomationRepairRisk.Destructive && !hasBackupEvidence
                    ? "backup_required"
                    : requiresConfirmation
                        ? "confirmation_required"
                        : "safe_auto_run";

        return new RepairAutomationDecision(
            normalizedRecipe,
            risk,
            known,
            withinRoot,
            requiresConfirmation,
            requiresBackup,
            RequiresVerification: true,
            rollbackAvailable,
            canAutoRun,
            reason);
    }

    public static AutomationRepairRisk RiskFor(string recipeId)
    {
        var normalized = NormalizeRecipeId(recipeId);
        return Recipes.TryGetValue(normalized, out var risk) ? risk : AutomationRepairRisk.Unknown;
    }

    public static bool IsWithinProjectRoot(string projectRoot, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            throw new ArgumentException("Project root is required.", nameof(projectRoot));
        }

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new ArgumentException("Repair target path is required.", nameof(targetPath));
        }

        var root = Path.GetFullPath(projectRoot);
        var target = Path.IsPathFullyQualified(targetPath)
            ? Path.GetFullPath(targetPath)
            : Path.GetFullPath(targetPath, root);
        var relative = Path.GetRelativePath(root, target);

        return relative != ".." &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !Path.IsPathFullyQualified(relative);
    }

    private static string NormalizeRecipeId(string recipeId)
    {
        if (string.IsNullOrWhiteSpace(recipeId) || recipeId.Length > 128 || recipeId.Any(char.IsControl))
        {
            throw new ArgumentException("Repair recipe ID is invalid.", nameof(recipeId));
        }

        return recipeId.Trim().ToLowerInvariant();
    }
}
