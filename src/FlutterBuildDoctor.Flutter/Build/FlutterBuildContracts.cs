using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Flutter.Commands;

namespace FlutterBuildDoctor.Flutter.Build;

public enum FlutterBuildArtifactType
{
    Apk = 0,
    AppBundle
}

public enum FlutterBuildMode
{
    Debug = 0,
    Profile,
    Release
}

public enum FlutterBuildStatus
{
    Succeeded = 0,
    Failed,
    Cancelled,
    TimedOut,
    ArtifactMissing,
    ArtifactInspectionFailed
}

public sealed record FlutterBuildRequest(
    FlutterCommandContext Context,
    FlutterBuildArtifactType ArtifactType,
    FlutterBuildMode Mode,
    string? Flavor = null,
    string? Target = null);

public sealed record FlutterBuildArtifact(
    FlutterBuildArtifactType Type,
    string Path,
    long SizeBytes,
    DateTimeOffset LastWriteTimeUtc,
    string? Sha256 = null);

public sealed record FlutterBuildAttempt(
    int AttemptNumber,
    ProcessExecutionStatus Status,
    int? ExitCode,
    TimeSpan Duration,
    string? FailureReason,
    string? RetryReason,
    ProcessExecutionReceipt? ExecutionReceipt);

public sealed record FlutterBuildReceipt(
    Guid BuildId,
    FlutterBuildRequest Request,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    FlutterBuildStatus Status,
    IReadOnlyList<FlutterBuildAttempt> Attempts,
    FlutterBuildArtifact? Artifact,
    string? FailureReason)
{
    public TimeSpan Duration => FinishedAt - StartedAt;
    public int AttemptCount => Attempts.Count;
    public bool IsSuccess => Status == FlutterBuildStatus.Succeeded;
}

public interface IFlutterBuildRequestBuilder
{
    ProcessRequest Build(FlutterBuildRequest request);
}

public interface IBuildArtifactLocator
{
    FlutterBuildArtifact? Locate(FlutterBuildRequest request);
}

public interface IArtifactHashService
{
    Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken = default);
}

public sealed record BuildRetryDecision(bool ShouldRetry, string? Reason = null);

public interface IBuildRetryPolicy
{
    BuildRetryDecision Evaluate(int completedAttempts, ProcessResult result);
}

public interface IFlutterBuildService
{
    Task<FlutterBuildReceipt> BuildAsync(
        FlutterBuildRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default);
}
