using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Application.Repairs;

namespace FlutterBuildDoctor.Application.UX;

public enum UiSemanticState
{
    Neutral = 0,
    Ready,
    Info,
    Warning,
    Error,
    Running,
    Disabled
}

public sealed record StatusPresentation(
    UiSemanticState State,
    string Label,
    string AutomationName,
    string IconKey,
    string PaletteToken);

public interface IStatusPresentationService
{
    StatusPresentation Present(UiSemanticState state, string? detail = null);
}

public sealed record KeyboardShortcut(
    string ActionId,
    string Gesture,
    string AccessibleLabel,
    int Order);

public interface IKeyboardWorkflowCatalog
{
    IReadOnlyList<KeyboardShortcut> GetPrimaryShortcuts();
    KeyboardShortcut? FindByAction(string actionId);
}

public sealed record FocusVisualTokens(
    double OutlineThickness,
    double CornerRadius,
    double Padding,
    string OutlinePaletteToken);

public enum ApplicationTheme
{
    Dark = 0,
    Light,
    HighContrast
}

public sealed record ThemePalette(
    ApplicationTheme Theme,
    string Background,
    string Surface,
    string SurfaceRaised,
    string TextPrimary,
    string TextSecondary,
    string Accent,
    string Focus,
    string Success,
    string Warning,
    string Error,
    string Border);

public interface IThemeCatalog
{
    ThemePalette Get(ApplicationTheme theme);
}

public sealed record LiveLogFilter(
    string? SearchText = null,
    ProcessStream? Stream = null,
    bool CaseSensitive = false);

public interface ILiveLogFilterService
{
    IReadOnlyList<ProcessOutputLine> Filter(
        IEnumerable<ProcessOutputLine> lines,
        LiveLogFilter filter);
}

public sealed record ProblemEvidence(
    string Title,
    string? Code,
    string Summary,
    IReadOnlyList<string> EvidenceLines,
    string? SuggestedAction = null);

public interface IProblemEvidenceFormatter
{
    string Format(ProblemEvidence evidence);
}

public enum WorkspaceVisualStateKind
{
    NoProject = 0,
    Empty,
    Loading,
    Ready,
    Error
}

public sealed record WorkspaceVisualState(
    WorkspaceVisualStateKind Kind,
    string Title,
    string Message,
    string? PrimaryActionId = null,
    string? PrimaryActionLabel = null,
    bool IsBusy = false);

public interface IWorkspaceStateFactory
{
    WorkspaceVisualState Create(
        WorkspaceVisualStateKind kind,
        string? detail = null,
        string? primaryActionId = null,
        string? primaryActionLabel = null);
}

public sealed record OperationCancellationState(
    bool CanCancel,
    bool IsCancellationRequested,
    string StatusText);

public interface IOperationCancellationController : IDisposable
{
    CancellationToken Token { get; }
    OperationCancellationState State { get; }
    void Start();
    bool RequestCancellation();
    void Complete();
}

public sealed record RiskConfirmationRequest(
    string Title,
    string Message,
    string ConfirmLabel,
    string CancelLabel,
    RepairRisk Risk,
    IReadOnlyList<string> Consequences,
    bool RollbackAvailable,
    string AutomationSummary);

public interface IRiskConfirmationRequestFactory
{
    RiskConfirmationRequest Create(RepairPlan plan);
}
