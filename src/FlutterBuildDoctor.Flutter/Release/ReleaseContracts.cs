using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Flutter.Build;
using FlutterBuildDoctor.Flutter.Commands;

namespace FlutterBuildDoctor.Flutter.Release;

public enum ReleaseCheckStatus
{
    Ready = 0,
    Warning,
    Blocker
}

public enum ReleaseExecutionStatus
{
    Blocked = 0,
    Succeeded,
    Failed,
    Cancelled
}

public sealed record ReleaseCheck(
    string Code,
    ReleaseCheckStatus Status,
    string Summary,
    IReadOnlyList<string> Evidence);

public sealed record ReleasePreflightReport(
    string ProjectRoot,
    IReadOnlyList<ReleaseCheck> Checks)
{
    public int BlockerCount => Checks.Count(static check => check.Status == ReleaseCheckStatus.Blocker);
    public int WarningCount => Checks.Count(static check => check.Status == ReleaseCheckStatus.Warning);
    public bool IsReady => BlockerCount == 0;
}

public sealed record ReleaseBuildRequest(
    FlutterCommandContext Context,
    string? Flavor = null,
    string? Target = null);

public sealed record ReleaseArtifactReceipt(
    FlutterBuildArtifactType Type,
    string Path,
    long SizeBytes,
    string Sha256);

public sealed record ReleaseReceipt(
    Guid ReleaseId,
    DateTimeOffset CreatedAt,
    ReleaseBuildRequest Request,
    FlutterBuildArtifactType ArtifactType,
    ReleaseExecutionStatus Status,
    ReleasePreflightReport Preflight,
    FlutterBuildReceipt? BuildReceipt,
    ReleaseArtifactReceipt? Artifact,
    string Message);

public interface IReleasePackageInspector
{
    ReleaseCheck Inspect(string projectRoot);
}

public interface IReleaseVersionInspector
{
    ReleaseCheck Inspect(string projectRoot);
}

public interface IReleaseSigningInspector
{
    ReleaseCheck Inspect(string projectRoot);
}

public interface IReleaseManifestInspector
{
    ReleaseCheck Inspect(string projectRoot);
}

public interface IReleasePreflightService
{
    ReleasePreflightReport Inspect(string projectRoot);
}

public interface IReleaseHistoryStore
{
    Task AddAsync(ReleaseReceipt receipt, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReleaseReceipt>> GetRecentAsync(int limit = 20, CancellationToken cancellationToken = default);
}

public interface IReleaseOutputActionService
{
    ProcessLaunchResult OpenOutputDirectory(string artifactPath);
    ProcessLaunchResult RevealArtifact(string artifactPath);
}

public interface IReleaseOrchestrator
{
    Task<ReleaseReceipt> BuildApkAsync(
        ReleaseBuildRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default);

    Task<ReleaseReceipt> BuildAppBundleAsync(
        ReleaseBuildRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default);
}
