using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Flutter.Build;

namespace FlutterBuildDoctor.Flutter.Release;

public sealed class ReleaseOrchestrator : IReleaseOrchestrator
{
    private readonly IReleasePreflightService _preflightService;
    private readonly IFlutterBuildService _buildService;
    private readonly IReleaseHistoryStore _historyStore;

    public ReleaseOrchestrator(
        IReleasePreflightService preflightService,
        IFlutterBuildService buildService,
        IReleaseHistoryStore historyStore)
    {
        _preflightService = preflightService ?? throw new ArgumentNullException(nameof(preflightService));
        _buildService = buildService ?? throw new ArgumentNullException(nameof(buildService));
        _historyStore = historyStore ?? throw new ArgumentNullException(nameof(historyStore));
    }

    public Task<ReleaseReceipt> BuildApkAsync(
        ReleaseBuildRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(request, FlutterBuildArtifactType.Apk, progress, cancellationToken);

    public Task<ReleaseReceipt> BuildAppBundleAsync(
        ReleaseBuildRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(request, FlutterBuildArtifactType.AppBundle, progress, cancellationToken);

    private async Task<ReleaseReceipt> ExecuteAsync(
        ReleaseBuildRequest request,
        FlutterBuildArtifactType artifactType,
        IProgress<ProcessOutputLine>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var preflight = _preflightService.Inspect(request.Context.WorkingDirectory);
        if (!preflight.IsReady)
        {
            var blocked = new ReleaseReceipt(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                request,
                artifactType,
                ReleaseExecutionStatus.Blocked,
                preflight,
                null,
                null,
                $"Release blocked by {preflight.BlockerCount} preflight check(s)." );
            await _historyStore.AddAsync(blocked, cancellationToken).ConfigureAwait(false);
            return blocked;
        }

        var build = await _buildService.BuildAsync(
            new FlutterBuildRequest(
                request.Context,
                artifactType,
                FlutterBuildMode.Release,
                request.Flavor,
                request.Target),
            progress,
            cancellationToken).ConfigureAwait(false);

        var status = build.Status == FlutterBuildStatus.Cancelled
            ? ReleaseExecutionStatus.Cancelled
            : build.IsSuccess ? ReleaseExecutionStatus.Succeeded : ReleaseExecutionStatus.Failed;
        var artifact = build.Artifact is { Sha256: not null } builtArtifact
            ? new ReleaseArtifactReceipt(
                builtArtifact.Type,
                builtArtifact.Path,
                builtArtifact.SizeBytes,
                builtArtifact.Sha256)
            : null;
        var receipt = new ReleaseReceipt(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            request,
            artifactType,
            status,
            preflight,
            build,
            artifact,
            status == ReleaseExecutionStatus.Succeeded
                ? "Release artifact built and verified."
                : build.FailureReason ?? $"Release build ended with status {build.Status}.");
        await _historyStore.AddAsync(receipt, cancellationToken).ConfigureAwait(false);
        return receipt;
    }
}
