namespace FlutterBuildDoctor.Flutter.Detection;

public sealed record FlutterDetectionResult(
    bool Installed,
    string? FlutterPath,
    string? FlutterVersion,
    string? DartVersion,
    string? Channel,
    string Message);
