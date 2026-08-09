using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Android.Detection;

public enum JavaDetectionStatus
{
    Succeeded = 0,
    Missing,
    ProbeFailed,
    TimedOut,
    Cancelled,
    MetadataInvalid
}

public sealed record JavaDetectionRequest(
    string? PathValue = null,
    string? PathExtValue = null,
    TimeSpan? ProbeTimeout = null);

public sealed record JavaInstallation(
    string ExecutablePath,
    string? JavaHome,
    string? Version,
    string? Vendor,
    string? Architecture,
    bool IsJdk,
    string? JavacPath,
    int PathIndex,
    int ResolutionOrder,
    bool IsPreferred,
    bool IsShadowed,
    string? Message = null,
    ProcessResult? ProbeResult = null);

public sealed record JavaDetectionResult(
    JavaDetectionStatus Status,
    JavaInstallation? PreferredInstallation,
    IReadOnlyList<JavaInstallation> Installations,
    bool HasConflict,
    PathExecutableDiscoveryResult PathDiscovery,
    string? Message = null)
{
    public bool IsSuccess => Status == JavaDetectionStatus.Succeeded;

    public bool Installed => Installations.Count > 0;
}

public interface IJavaInstallationDetector
{
    Task<JavaDetectionResult> DetectAsync(
        JavaDetectionRequest? request = null,
        CancellationToken cancellationToken = default);
}
