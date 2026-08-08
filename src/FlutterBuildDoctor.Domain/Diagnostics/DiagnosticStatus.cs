namespace FlutterBuildDoctor.Domain.Diagnostics;

public enum DiagnosticStatus
{
    Unknown = 0,
    Ready,
    Running,
    Missing,
    Outdated,
    Incompatible,
    Broken,
    Warning,
    Error,
    Cancelled,
    RepairAvailable,
    Fixed
}

public enum DiagnosticSeverity
{
    Info = 0,
    Warning,
    Error,
    Critical
}

public sealed record DiagnosticItem(
    string Id,
    string Name,
    DiagnosticStatus Status,
    DiagnosticSeverity Severity,
    string? InstalledVersion = null,
    string? RequiredVersion = null,
    string? RecommendedVersion = null,
    string? Path = null,
    string? Summary = null,
    string? Evidence = null,
    bool CanRepair = false);
