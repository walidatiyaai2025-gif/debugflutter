using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Flutter.Build;

public sealed class FlutterBuildService : IFlutterBuildService
{
    private readonly IProcessRunner _processRunner;
    private readonly IFlutterBuildRequestBuilder _requestBuilder;
    private readonly IBuildArtifactLocator _artifactLocator;
    private readonly IArtifactHashService _hashService;
    private readonly IBuildRetryPolicy _retryPolicy;

    public FlutterBuildService(
        IProcessRunner processRunner,
        IFlutterBuildRequestBuilder requestBuilder,
        IBuildArtifactLocator artifactLocator,
        IArtifactHashService hashService,
        IBuildRetryPolicy retryPolicy)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _requestBuilder = requestBuilder ?? throw new ArgumentNullException(nameof(requestBuilder));
        _artifactLocator = artifactLocator ?? throw new ArgumentNullException(nameof(artifactLocator));
        _hashService = hashService ?? throw new ArgumentNullException(nameof(hashService));
        _retryPolicy = retryPolicy ?? throw new ArgumentNullException(nameof(retryPolicy));
    }

    public async Task<FlutterBuildReceipt> BuildAsync(
        FlutterBuildRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var buildId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;
        var attempts = new List<FlutterBuildAttempt>();
        ProcessResult? lastResult = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var processRequest = _requestBuilder.Build(request);
            lastResult = await _processRunner.RunAsync(processRequest, progress, cancellationToken).ConfigureAwait(false);
            var attemptNumber = attempts.Count + 1;
            var retry = _retryPolicy.Evaluate(attemptNumber, lastResult);
            attempts.Add(ToAttempt(attemptNumber, lastResult, retry.ShouldRetry ? retry.Reason : null));

            if (lastResult.IsSuccess || !retry.ShouldRetry)
            {
                break;
            }
        }

        var finishedProcessAt = DateTimeOffset.UtcNow;
        if (lastResult is null)
        {
            return Receipt(
                buildId,
                request,
                startedAt,
                finishedProcessAt,
                FlutterBuildStatus.Failed,
                attempts,
                null,
                "Build process did not produce a result.");
        }

        if (!lastResult.IsSuccess)
        {
            return Receipt(
                buildId,
                request,
                startedAt,
                finishedProcessAt,
                MapStatus(lastResult.Status),
                attempts,
                null,
                lastResult.FailureReason ?? $"Flutter build ended with status {lastResult.Status}.");
        }

        var artifact = _artifactLocator.Locate(request);
        if (artifact is null)
        {
            return Receipt(
                buildId,
                request,
                startedAt,
                DateTimeOffset.UtcNow,
                FlutterBuildStatus.ArtifactMissing,
                attempts,
                null,
                "Flutter reported success, but no matching build artifact was found under the project build directory.");
        }

        try
        {
            var hash = await _hashService.ComputeSha256Async(artifact.Path, cancellationToken).ConfigureAwait(false);
            artifact = artifact with { Sha256 = hash };
            return Receipt(
                buildId,
                request,
                startedAt,
                DateTimeOffset.UtcNow,
                FlutterBuildStatus.Succeeded,
                attempts,
                artifact,
                null);
        }
        catch (OperationCanceledException)
        {
            return Receipt(
                buildId,
                request,
                startedAt,
                DateTimeOffset.UtcNow,
                FlutterBuildStatus.Cancelled,
                attempts,
                artifact,
                "Artifact hashing was cancelled.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Receipt(
                buildId,
                request,
                startedAt,
                DateTimeOffset.UtcNow,
                FlutterBuildStatus.ArtifactInspectionFailed,
                attempts,
                artifact,
                $"Build artifact could not be hashed: {ex.Message}");
        }
    }

    private static FlutterBuildAttempt ToAttempt(
        int attemptNumber,
        ProcessResult result,
        string? retryReason)
        => new(
            attemptNumber,
            result.Status,
            result.ExitCode,
            result.Duration,
            result.FailureReason,
            retryReason,
            result.ExecutionReceipt);

    private static FlutterBuildStatus MapStatus(ProcessExecutionStatus status)
        => status switch
        {
            ProcessExecutionStatus.Cancelled => FlutterBuildStatus.Cancelled,
            ProcessExecutionStatus.TimedOut => FlutterBuildStatus.TimedOut,
            _ => FlutterBuildStatus.Failed
        };

    private static FlutterBuildReceipt Receipt(
        Guid buildId,
        FlutterBuildRequest request,
        DateTimeOffset startedAt,
        DateTimeOffset finishedAt,
        FlutterBuildStatus status,
        IReadOnlyList<FlutterBuildAttempt> attempts,
        FlutterBuildArtifact? artifact,
        string? failureReason)
        => new(
            buildId,
            request,
            startedAt,
            finishedAt,
            status,
            attempts.ToArray(),
            artifact,
            failureReason);
}
