namespace FlutterBuildDoctor.Domain.Diagnostics;

public sealed record DiagnosticRecord
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Category { get; init; }
    public required DiagnosticSeverity Severity { get; init; }
    public string? Evidence { get; init; }
    public string? RootCause { get; init; }
    public bool RepairAvailable { get; init; }
}
