using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Application.Repairs;
using FlutterBuildDoctor.Application.UX;

namespace FlutterBuildDoctor.UnitTests.UX;

public sealed class UxServicesTests
{
    [Theory]
    [InlineData(UiSemanticState.Ready, "Ready", "Status.Success")]
    [InlineData(UiSemanticState.Warning, "Needs attention", "Status.Warning")]
    [InlineData(UiSemanticState.Error, "Blocked", "Status.Error")]
    public void StatusPresentation_ProvidesTextAndAutomationSemantics(UiSemanticState state, string label, string token)
    {
        var presentation = new StatusPresentationService().Present(state, "Android SDK");

        Assert.Equal(label, presentation.Label);
        Assert.Equal(token, presentation.PaletteToken);
        Assert.Contains("Android SDK", presentation.AutomationName, StringComparison.Ordinal);
    }

    [Fact]
    public void KeyboardCatalog_HasUniquePrimaryGesturesAndEscapeCancellation()
    {
        var shortcuts = new KeyboardWorkflowCatalog().GetPrimaryShortcuts();

        Assert.Equal(shortcuts.Count, shortcuts.Select(item => item.Gesture).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal("Escape", shortcuts.Single(item => item.ActionId == "cancel-operation").Gesture);
    }

    [Fact]
    public void ThemeCatalog_HighContrastKeepsStrongDistinctTokens()
    {
        var palette = new ThemeCatalog().Get(ApplicationTheme.HighContrast);

        Assert.Equal("#000000", palette.Background);
        Assert.Equal("#FFFFFF", palette.TextPrimary);
        Assert.NotEqual(palette.Background, palette.Focus);
        Assert.NotEqual(palette.Background, palette.Error);
        Assert.Equal(2, FocusVisualDefaults.CreateAccessible().OutlineThickness);
    }

    [Fact]
    public void LiveLogFilter_FiltersBySearchAndStreamWithoutMutatingInput()
    {
        var now = DateTimeOffset.UtcNow;
        var lines = new[]
        {
            new ProcessOutputLine(now, ProcessStream.StdOut, "Flutter ready"),
            new ProcessOutputLine(now, ProcessStream.StdErr, "Gradle ERROR"),
            new ProcessOutputLine(now, ProcessStream.StdErr, "warning only")
        };

        var result = new LiveLogFilterService().Filter(lines, new LiveLogFilter("error", ProcessStream.StdErr));

        Assert.Single(result);
        Assert.Equal("Gradle ERROR", result[0].Text);
        Assert.Equal(3, lines.Length);
    }

    [Fact]
    public void EvidenceFormatter_ProducesCopyableStructuredEvidence()
    {
        var text = new ProblemEvidenceFormatter().Format(new ProblemEvidence(
            "Gradle mismatch",
            "GRADLE_AGP",
            "AGP requires a newer Gradle wrapper.",
            new[] { "AGP 8.9", "Gradle 8.10" },
            "Upgrade Gradle."));

        Assert.Contains("Code: GRADLE_AGP", text, StringComparison.Ordinal);
        Assert.Contains("- AGP 8.9", text, StringComparison.Ordinal);
        Assert.Contains("Suggested action: Upgrade Gradle.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceStates_AreConsistentForNoProjectLoadingAndError()
    {
        var factory = new WorkspaceStateFactory();

        Assert.Equal("No project selected", factory.Create(WorkspaceVisualStateKind.NoProject).Title);
        Assert.True(factory.Create(WorkspaceVisualStateKind.Loading).IsBusy);
        Assert.Equal("Action required", factory.Create(WorkspaceVisualStateKind.Error).Title);
    }

    [Fact]
    public void CancellationController_ExposesSingleCancellableLifecycle()
    {
        using var controller = new OperationCancellationController();
        controller.Start();
        var token = controller.Token;

        Assert.True(controller.State.CanCancel);
        Assert.True(controller.RequestCancellation());
        Assert.True(token.IsCancellationRequested);
        Assert.False(controller.State.CanCancel);
        Assert.False(controller.RequestCancellation());
        controller.Complete();
        Assert.False(controller.State.IsCancellationRequested);
    }

    [Fact]
    public void RiskConfirmation_IncludesConsequencesAndRollbackState()
    {
        var plan = new RepairPlan(
            "repair.demo",
            "Repair demo",
            IssueSignature.Create("DEMO", "Test", "evidence"),
            RepairRisk.Risky,
            new[]
            {
                new RepairActionPreview("a", "Change file", RepairRisk.Risky, new[] { "file" }, false, true, "Configuration will change.")
            },
            RequiresConfirmation: true,
            RollbackSupported: true,
            new[] { "Verify output." });

        var request = new RiskConfirmationRequestFactory().Create(plan);

        Assert.Equal(RepairRisk.Risky, request.Risk);
        Assert.True(request.RollbackAvailable);
        Assert.Contains("Configuration will change.", request.Consequences);
        Assert.Contains("Rollback is available", request.AutomationSummary, StringComparison.Ordinal);
    }
}
