using System.Text;
using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Application.Repairs;

namespace FlutterBuildDoctor.Application.UX;

public sealed class StatusPresentationService : IStatusPresentationService
{
    public StatusPresentation Present(UiSemanticState state, string? detail = null)
    {
        var (label, icon, palette) = state switch
        {
            UiSemanticState.Ready => ("Ready", "Status.Ready", "Status.Success"),
            UiSemanticState.Info => ("Information", "Status.Info", "Status.Info"),
            UiSemanticState.Warning => ("Needs attention", "Status.Warning", "Status.Warning"),
            UiSemanticState.Error => ("Blocked", "Status.Error", "Status.Error"),
            UiSemanticState.Running => ("Running", "Status.Running", "Status.Info"),
            UiSemanticState.Disabled => ("Unavailable", "Status.Disabled", "Text.Disabled"),
            _ => ("Not evaluated", "Status.Neutral", "Text.Secondary")
        };
        var automation = string.IsNullOrWhiteSpace(detail) ? label : $"{label}. {detail.Trim()}";
        return new StatusPresentation(state, label, automation, icon, palette);
    }
}

public sealed class KeyboardWorkflowCatalog : IKeyboardWorkflowCatalog
{
    private static readonly KeyboardShortcut[] Shortcuts =
    {
        new("home", "Ctrl+1", "Go to Home dashboard", 1),
        new("repository", "Ctrl+2", "Go to Repository workspace", 2),
        new("environment", "Ctrl+3", "Go to Environment Doctor", 3),
        new("compatibility", "Ctrl+4", "Go to Compatibility checks", 4),
        new("commands", "Ctrl+5", "Go to Flutter Command Center", 5),
        new("build", "Ctrl+6", "Go to Build Center", 6),
        new("devices", "Ctrl+7", "Go to Devices and Emulators", 7),
        new("problems", "Ctrl+8", "Go to Problems workspace", 8),
        new("settings", "Ctrl+9", "Go to Settings", 9),
        new("cancel-operation", "Escape", "Cancel the active operation when cancellation is available", 10)
    };

    public IReadOnlyList<KeyboardShortcut> GetPrimaryShortcuts() => Shortcuts;

    public KeyboardShortcut? FindByAction(string actionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        return Shortcuts.FirstOrDefault(shortcut =>
            string.Equals(shortcut.ActionId, actionId, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class ThemeCatalog : IThemeCatalog
{
    private static readonly IReadOnlyDictionary<ApplicationTheme, ThemePalette> Palettes =
        new Dictionary<ApplicationTheme, ThemePalette>
        {
            [ApplicationTheme.Dark] = new(
                ApplicationTheme.Dark,
                "#070B14", "#0D1424", "#121C31", "#F5F7FB", "#AAB5C8",
                "#4EA1FF", "#8AC7FF", "#4FD18B", "#F5C451", "#FF6B7A", "#263652"),
            [ApplicationTheme.Light] = new(
                ApplicationTheme.Light,
                "#F5F7FB", "#FFFFFF", "#EDF2F8", "#101827", "#536176",
                "#0B67D1", "#004A9F", "#087A44", "#8A5A00", "#B42334", "#C7D0DD"),
            [ApplicationTheme.HighContrast] = new(
                ApplicationTheme.HighContrast,
                "#000000", "#000000", "#111111", "#FFFFFF", "#FFFFFF",
                "#00FFFF", "#FFFF00", "#00FF00", "#FFFF00", "#FF4D4D", "#FFFFFF")
        };

    public ThemePalette Get(ApplicationTheme theme)
        => Palettes.TryGetValue(theme, out var palette)
            ? palette
            : throw new ArgumentOutOfRangeException(nameof(theme), theme, "Unknown application theme.");
}

public sealed class LiveLogFilterService : ILiveLogFilterService
{
    public IReadOnlyList<ProcessOutputLine> Filter(
        IEnumerable<ProcessOutputLine> lines,
        LiveLogFilter filter)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(filter);
        var query = filter.SearchText?.Trim();
        var comparison = filter.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return lines
            .Where(line => filter.Stream is null || line.Stream == filter.Stream)
            .Where(line => string.IsNullOrEmpty(query) || line.Text.Contains(query, comparison))
            .ToArray();
    }
}

public sealed class ProblemEvidenceFormatter : IProblemEvidenceFormatter
{
    public string Format(ProblemEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidence.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidence.Summary);
        var builder = new StringBuilder();
        builder.AppendLine(evidence.Title.Trim());
        if (!string.IsNullOrWhiteSpace(evidence.Code)) builder.AppendLine($"Code: {evidence.Code.Trim()}");
        builder.AppendLine(evidence.Summary.Trim());
        if (evidence.EvidenceLines.Count > 0)
        {
            builder.AppendLine("Evidence:");
            foreach (var line in evidence.EvidenceLines.Where(static line => !string.IsNullOrWhiteSpace(line)))
                builder.AppendLine($"- {line.Trim()}");
        }
        if (!string.IsNullOrWhiteSpace(evidence.SuggestedAction))
            builder.AppendLine($"Suggested action: {evidence.SuggestedAction.Trim()}");
        return builder.ToString().TrimEnd();
    }
}

public sealed class WorkspaceStateFactory : IWorkspaceStateFactory
{
    public WorkspaceVisualState Create(
        WorkspaceVisualStateKind kind,
        string? detail = null,
        string? primaryActionId = null,
        string? primaryActionLabel = null)
    {
        var (title, message, busy) = kind switch
        {
            WorkspaceVisualStateKind.NoProject => ("No project selected", "Import or select a Flutter project to continue.", false),
            WorkspaceVisualStateKind.Empty => ("Nothing to show", "No results are available for the current project and filters.", false),
            WorkspaceVisualStateKind.Loading => ("Working…", "Flutter Build Doctor is processing the current operation.", true),
            WorkspaceVisualStateKind.Ready => ("Ready", "The workspace is ready.", false),
            WorkspaceVisualStateKind.Error => ("Action required", "The operation could not complete successfully.", false),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown workspace visual state.")
        };
        if (!string.IsNullOrWhiteSpace(detail)) message = $"{message} {detail.Trim()}";
        return new WorkspaceVisualState(kind, title, message, primaryActionId, primaryActionLabel, busy);
    }
}

public sealed class OperationCancellationController : IOperationCancellationController
{
    private CancellationTokenSource? _source;

    public CancellationToken Token => _source?.Token ?? CancellationToken.None;

    public OperationCancellationState State { get; private set; } = new(false, false, "No cancellable operation is active.");

    public void Start()
    {
        _source?.Dispose();
        _source = new CancellationTokenSource();
        State = new OperationCancellationState(true, false, "Operation is running. Press Escape or use Cancel to stop it.");
    }

    public bool RequestCancellation()
    {
        if (_source is null || _source.IsCancellationRequested) return false;
        _source.Cancel();
        State = new OperationCancellationState(false, true, "Cancellation requested…");
        return true;
    }

    public void Complete()
    {
        _source?.Dispose();
        _source = null;
        State = new OperationCancellationState(false, false, "Operation complete.");
    }

    public void Dispose()
    {
        _source?.Cancel();
        _source?.Dispose();
        _source = null;
    }
}

public sealed class RiskConfirmationRequestFactory : IRiskConfirmationRequestFactory
{
    public RiskConfirmationRequest Create(RepairPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var consequences = plan.Actions
            .Select(action => action.Consequence)
            .Where(static consequence => !string.IsNullOrWhiteSpace(consequence))
            .Select(static consequence => consequence!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var riskLabel = plan.Risk switch
        {
            RepairRisk.Destructive => "Destructive change",
            RepairRisk.Risky => "Risky change",
            _ => plan.Actions.Any(static action => action.IsDestructive) ? "Generated data will be removed" : "Safe action"
        };
        var rollback = plan.RollbackSupported ? "Rollback is available." : "Automatic rollback is not available.";
        return new RiskConfirmationRequest(
            plan.Title,
            $"{riskLabel}. Review {plan.Actions.Count} planned action(s) before continuing. {rollback}",
            plan.Risk == RepairRisk.Destructive ? "Apply destructive repair" : "Apply repair",
            "Cancel",
            plan.Risk,
            consequences,
            plan.RollbackSupported,
            $"{plan.Title}. {riskLabel}. {rollback}");
    }
}

public static class FocusVisualDefaults
{
    public static FocusVisualTokens CreateAccessible()
        => new(2, 4, 2, "Focus.Primary");
}
