namespace FlutterBuildDoctor.Application.Environment.Detectors;

public sealed record ToolDetectionResult(
    string ToolName,
    bool Installed,
    string? Version,
    string? ExecutablePath,
    string Message);
