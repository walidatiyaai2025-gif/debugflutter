namespace FlutterBuildDoctor.Domain.Environment;

public sealed record ToolStatus(
    string Name,
    bool Installed,
    string? Version,
    string? Path,
    string? Message);
