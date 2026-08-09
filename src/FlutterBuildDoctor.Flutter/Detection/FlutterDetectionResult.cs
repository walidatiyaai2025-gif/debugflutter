using FlutterBuildDoctor.Application.Environment;

namespace FlutterBuildDoctor.Flutter.Detection;

public enum FlutterSdkDetectionStatus
{
    Succeeded = 0,
    Missing,
    InvalidSdkLayout,
    MetadataMissing,
    MetadataInvalid,
    Cancelled
}

public enum FlutterVersionMetadataSource
{
    None = 0,
    CachedVersionJson,
    LegacyVersionAndGitHead
}

public sealed record FlutterSdkDetectionRequest(
    string? PathValue = null,
    string? PathExtValue = null);

public sealed record FlutterSdkCandidate(
    string ExecutablePath,
    string? SdkRoot,
    int PathIndex,
    int ResolutionOrder,
    bool IsPreferred,
    bool IsShadowed,
    bool HasExpectedSdkLayout,
    string? VersionMetadataPath);

public sealed record FlutterDetectionResult(
    FlutterSdkDetectionStatus Status,
    bool Installed,
    string? FlutterPath,
    string? FlutterSdkPath,
    string? FlutterVersion,
    string? Channel,
    IReadOnlyList<FlutterSdkCandidate> Candidates,
    bool HasConflict,
    FlutterVersionMetadataSource MetadataSource = FlutterVersionMetadataSource.None,
    string? Message = null,
    string? RawMetadata = null,
    PathExecutableDiscoveryResult? PathDiscovery = null)
{
    public bool IsSuccess => Status == FlutterSdkDetectionStatus.Succeeded;
}

public interface IFlutterSdkDetector
{
    Task<FlutterDetectionResult> DetectAsync(
        FlutterSdkDetectionRequest? request = null,
        CancellationToken cancellationToken = default);
}
