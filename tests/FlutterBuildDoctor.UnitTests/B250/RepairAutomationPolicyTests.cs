using FlutterBuildDoctor.Application.Repairs;

namespace FlutterBuildDoctor.UnitTests.B250;

public sealed class RepairAutomationPolicyTests
{
    [Theory]
    [InlineData("repair.flutter-clean", AutomationRepairRisk.Safe)]
    [InlineData("repair.gradle-cache", AutomationRepairRisk.Risky)]
    [InlineData("repair.generated-clean", AutomationRepairRisk.Destructive)]
    [InlineData("repair.unknown", AutomationRepairRisk.Unknown)]
    public void RiskFor_MapsKnownAndUnknownRecipes(string recipe, AutomationRepairRisk expected)
    {
        Assert.Equal(expected, RepairAutomationPolicy.RiskFor(recipe));
    }

    [Fact]
    public void Decide_SafeRepairCanAutoRunAndAlwaysRequiresVerification()
    {
        var decision = RepairAutomationPolicy.Decide(
            "repair.flutter-clean",
            @"C:\work\app",
            @"C:\work\app\build");

        Assert.True(decision.KnownRecipe);
        Assert.True(decision.WithinProjectRoot);
        Assert.False(decision.RequiresConfirmation);
        Assert.False(decision.RequiresBackup);
        Assert.True(decision.RequiresVerification);
        Assert.False(decision.RollbackAvailable);
        Assert.True(decision.CanAutoRun);
        Assert.Equal("safe_auto_run", decision.ReasonCode);
    }

    [Fact]
    public void Decide_RiskyAndDestructiveRepairsRequireExplicitSafetyControls()
    {
        var risky = RepairAutomationPolicy.Decide(
            "repair.gradle-cache",
            @"C:\work\app",
            @"C:\work\app\.gradle");
        var destructive = RepairAutomationPolicy.Decide(
            "repair.generated-clean",
            @"C:\work\app",
            @"C:\work\app\build",
            hasBackupEvidence: false);
        var destructiveWithBackup = RepairAutomationPolicy.Decide(
            "repair.generated-clean",
            @"C:\work\app",
            @"C:\work\app\build",
            hasBackupEvidence: true);

        Assert.True(risky.RequiresConfirmation);
        Assert.False(risky.RequiresBackup);
        Assert.False(risky.CanAutoRun);
        Assert.Equal("confirmation_required", risky.ReasonCode);

        Assert.True(destructive.RequiresConfirmation);
        Assert.True(destructive.RequiresBackup);
        Assert.False(destructive.RollbackAvailable);
        Assert.Equal("backup_required", destructive.ReasonCode);

        Assert.True(destructiveWithBackup.RollbackAvailable);
        Assert.Equal("confirmation_required", destructiveWithBackup.ReasonCode);
    }

    [Fact]
    public void Decide_DeniesUnknownRecipesAndEscapingPathsWithStableReasonCodes()
    {
        var unknown = RepairAutomationPolicy.Decide(
            "repair.nope",
            @"C:\work\app",
            @"C:\work\app\build");
        var escaped = RepairAutomationPolicy.Decide(
            "repair.flutter-clean",
            @"C:\work\app",
            @"C:\work\outside");

        Assert.False(unknown.KnownRecipe);
        Assert.False(unknown.CanAutoRun);
        Assert.Equal("unknown_recipe", unknown.ReasonCode);

        Assert.False(escaped.WithinProjectRoot);
        Assert.False(escaped.CanAutoRun);
        Assert.Equal("path_escape", escaped.ReasonCode);
    }
}
