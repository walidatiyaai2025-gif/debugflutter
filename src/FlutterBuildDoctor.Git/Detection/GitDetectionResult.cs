namespace FlutterBuildDoctor.Git.Detection;

public sealed record GitDetectionResult(
    bool Installed,
    string? GitPath,
    string? Version,
    string Message);
