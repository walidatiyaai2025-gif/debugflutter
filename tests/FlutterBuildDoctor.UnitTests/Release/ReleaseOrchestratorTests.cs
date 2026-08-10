using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Flutter.Build;
using FlutterBuildDoctor.Flutter.Commands;
using FlutterBuildDoctor.Flutter.Release;

namespace FlutterBuildDoctor.UnitTests.Release;

public sealed class ReleaseOrchestratorTests
{
    [Theory]
    [InlineData(FlutterBuildArtifactType.Apk)]
    [InlineData(FlutterBuildArtifactType.AppBundle)]
    public async Task Orchestrator_BuildsReleaseArtifactAndRecordsHistory(FlutterBuildArtifactType type)
    {
        var preflight = new StubPreflight(ready: true);
        var build = new StubBuildService(type);
        var history = new InMemoryReleaseHistoryStore();
        var orchestrator = new ReleaseOrchestrator(preflight, build, history);
        var request = new ReleaseBuildRequest(new FlutterCommandContext("flutter", @"C:\work\app"), "prod", "lib/main_prod.dart");

        var receipt = type == FlutterBuildArtifactType.Apk
            ? await orchestrator.BuildApkAsync(request)
            : await orchestrator.BuildAppBundleAsync(request);

        Assert.Equal(ReleaseExecutionStatus.Succeeded, receipt.Status);
        Assert.NotNull(receipt.Artifact);
        Assert.Equal(type, build.LastRequest!.ArtifactType);
        Assert.Equal(FlutterBuildMode.Release, build.LastRequest.Mode);
        Assert.Equal("prod", build.LastRequest.Flavor);
        Assert.Equal("lib/main_prod.dart", build.LastRequest.Target);
        Assert.Single(await history.GetRecentAsync());
    }

    [Fact]
    public async Task Orchestrator_DoesNotRunBuildWhenPreflightHasBlocker()
    {
        var build = new StubBuildService(FlutterBuildArtifactType.Apk);
        var orchestrator = new ReleaseOrchestrator(new StubPreflight(ready: false), build, new InMemoryReleaseHistoryStore());

        var receipt = await orchestrator.BuildApkAsync(new ReleaseBuildRequest(new FlutterCommandContext("flutter", @"C:\work\app")));

        Assert.Equal(ReleaseExecutionStatus.Blocked, receipt.Status);
        Assert.Null(build.LastRequest);
    }

    private sealed class StubPreflight : IReleasePreflightService
    {
        private readonly bool _ready;
        public StubPreflight(bool ready) => _ready = ready;

        public ReleasePreflightReport Inspect(string projectRoot)
            => new(
                projectRoot,
                new[]
                {
                    new ReleaseCheck(
                        "preflight",
                        _ready ? ReleaseCheckStatus.Ready : ReleaseCheckStatus.Blocker,
                        _ready ? "ready" : "blocked",
                        Array.Empty<string>())
                });
    }

    private sealed class StubBuildService : IFlutterBuildService
    {
        private readonly FlutterBuildArtifactType _type;
        public StubBuildService(FlutterBuildArtifactType type) => _type = type;
        public FlutterBuildRequest? LastRequest { get; private set; }

        public Task<FlutterBuildReceipt> BuildAsync(
            FlutterBuildRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            var now = DateTimeOffset.UtcNow;
            var artifact = new FlutterBuildArtifact(_type, _type == FlutterBuildArtifactType.Apk ? "app-release.apk" : "app-release.aab", 1234, now, new string('a', 64));
            return Task.FromResult(new FlutterBuildReceipt(
                Guid.NewGuid(),
                request,
                now,
                now.AddSeconds(1),
                FlutterBuildStatus.Succeeded,
                Array.Empty<FlutterBuildAttempt>(),
                artifact,
                null));
        }
    }
}
