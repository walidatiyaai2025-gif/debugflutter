using FlutterBuildDoctor.Application.Environment;

namespace FlutterBuildDoctor.Flutter.Detection;

public enum DartSdkDetectionStatus
{
    Succeeded = 0,
    Missing,
    MetadataMissing,
    MetadataInvalid,
    Cancelled
}

public sealed record DartSdkDetectionRequest(string? PathValue = null, string? PathExtValue = null);

public sealed record DartSdkCandidate(
    string ExecutablePath,
    string? SdkRoot,
    string? Version,
    bool IsFlutterBundled,
    bool IsPathPreferred,
    bool IsShadowed,
    string? VersionMetadataPath,
    string? RawVersionMetadata,
    string? Message)
{
    public bool IsUsable => !string.IsNullOrWhiteSpace(ExecutablePath) && !string.IsNullOrWhiteSpace(Version);
}

public sealed record DartDetectionResult(
    DartSdkDetectionStatus Status,
    string? FlutterSdkPath,
    DartSdkCandidate? FlutterBundledCandidate,
    DartSdkCandidate? PathPreferredCandidate,
    IReadOnlyList<DartSdkCandidate> Candidates,
    bool HasPathConflict,
    bool HasFlutterPathMismatch,
    string Message,
    PathExecutableDiscoveryResult? PathDiscovery = null)
{
    public bool IsSuccess => Status == DartSdkDetectionStatus.Succeeded;
}

public interface IDartSdkDetector
{
    Task<DartDetectionResult> DetectAsync(
        FlutterDetectionResult flutterResult,
        DartSdkDetectionRequest? request = null,
        CancellationToken cancellationToken = default);
}
