using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Flutter.Detection;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class DartSdkDetectorTests
{
    [Fact]
    public async Task DetectAsync_FlutterBundledDartOnPath_LinksSingleCandidate()
    {
        using var fixture = new DartFixture();
        var bundled = fixture.CreateBundledDart("3.9.0");
        var discovery = new StubPathDiscovery(PathResult(bundled.ExecutablePath));
        var detector = new DartSdkDetector(discovery);

        var result = await detector.DetectAsync(FlutterResult(fixture.FlutterSdkRoot));

        Assert.True(result.IsSuccess, result.Message);
        var candidate = Assert.Single(result.Candidates);
        Assert.True(candidate.IsFlutterBundled);
        Assert.True(candidate.IsPathPreferred);
        Assert.Equal("3.9.0", candidate.Version);
        Assert.Equal(bundled.SdkRoot, candidate.SdkRoot, ignoreCase: true);
        Assert.False(result.HasFlutterPathMismatch);
        Assert.Same(candidate, result.FlutterBundledCandidate);
        Assert.Same(candidate, result.PathPreferredCandidate);
    }

    [Fact]
    public async Task DetectAsync_StandalonePathPreferredAndFlutterBundled_PreservesMismatch()
    {
        using var fixture = new DartFixture();
        var bundled = fixture.CreateBundledDart("3.9.0");
        var standalone = fixture.CreateStandaloneDart("standalone", "3.8.1");
        var detector = new DartSdkDetector(new StubPathDiscovery(PathResult(standalone.ExecutablePath)));

        var result = await detector.DetectAsync(FlutterResult(fixture.FlutterSdkRoot));

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(2, result.Candidates.Count);
        Assert.True(result.HasFlutterPathMismatch);
        Assert.Equal(bundled.ExecutablePath, result.FlutterBundledCandidate!.ExecutablePath, ignoreCase: true);
        Assert.Equal("3.9.0", result.FlutterBundledCandidate.Version);
        Assert.Equal(standalone.ExecutablePath, result.PathPreferredCandidate!.ExecutablePath, ignoreCase: true);
        Assert.Equal("3.8.1", result.PathPreferredCandidate.Version);
        Assert.Contains("differs from Flutter's bundled Dart", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DetectAsync_MultiplePathDarts_PreservesConflictAndShadowing()
    {
        using var fixture = new DartFixture();
        var first = fixture.CreateStandaloneDart("first", "3.8.0");
        var second = fixture.CreateStandaloneDart("second", "3.7.5");
        var detector = new DartSdkDetector(new StubPathDiscovery(PathResult(first.ExecutablePath, second.ExecutablePath)));

        var result = await detector.DetectAsync(FlutterResult(installed: false, sdkRoot: null));

        Assert.True(result.IsSuccess, result.Message);
        Assert.True(result.HasPathConflict);
        Assert.False(result.HasFlutterPathMismatch);
        Assert.Equal(2, result.Candidates.Count);
        Assert.True(result.PathPreferredCandidate!.IsPathPreferred);
        Assert.Contains(result.Candidates, candidate => candidate.IsShadowed);
        Assert.Contains("Multiple Dart executables", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DetectAsync_BundledDartNotOnPath_StillSucceedsAndLinksFlutter()
    {
        using var fixture = new DartFixture();
        var bundled = fixture.CreateBundledDart("3.9.1");
        var detector = new DartSdkDetector(new StubPathDiscovery(PathResult()));

        var result = await detector.DetectAsync(FlutterResult(fixture.FlutterSdkRoot));

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(bundled.ExecutablePath, result.FlutterBundledCandidate!.ExecutablePath, ignoreCase: true);
        Assert.Null(result.PathPreferredCandidate);
        Assert.False(result.HasFlutterPathMismatch);
    }

    [Fact]
    public async Task DetectAsync_FlutterMetadataIncompleteButInstalled_StillFindsBundledDart()
    {
        using var fixture = new DartFixture();
        fixture.CreateBundledDart("3.9.2");
        var detector = new DartSdkDetector(new StubPathDiscovery(PathResult()));
        var flutter = FlutterResult(fixture.FlutterSdkRoot) with
        {
            Status = FlutterSdkDetectionStatus.MetadataMissing,
            FlutterVersion = null,
            Channel = null
        };

        var result = await detector.DetectAsync(flutter);

        Assert.True(result.IsSuccess, result.Message);
        Assert.True(result.FlutterBundledCandidate!.IsFlutterBundled);
        Assert.Equal("3.9.2", result.FlutterBundledCandidate.Version);
    }

    [Fact]
    public async Task DetectAsync_StandaloneOnly_SucceedsWithoutFlutterLink()
    {
        using var fixture = new DartFixture();
        var standalone = fixture.CreateStandaloneDart("standalone", "3.6.2");
        var detector = new DartSdkDetector(new StubPathDiscovery(PathResult(standalone.ExecutablePath)));

        var result = await detector.DetectAsync(FlutterResult(installed: false, sdkRoot: null));

        Assert.True(result.IsSuccess, result.Message);
        Assert.Null(result.FlutterBundledCandidate);
        Assert.Equal("3.6.2", result.PathPreferredCandidate!.Version);
    }

    [Fact]
    public async Task DetectAsync_NoDart_ReturnsMissing()
    {
        using var fixture = new DartFixture();
        var detector = new DartSdkDetector(new StubPathDiscovery(PathResult()));

        var result = await detector.DetectAsync(FlutterResult(fixture.FlutterSdkRoot));

        Assert.Equal(DartSdkDetectionStatus.Missing, result.Status);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task DetectAsync_DartWithoutVersionFile_ReturnsMetadataMissing()
    {
        using var fixture = new DartFixture();
        var standalone = fixture.CreateStandaloneDart("no-version", version: null);
        var detector = new DartSdkDetector(new StubPathDiscovery(PathResult(standalone.ExecutablePath)));

        var result = await detector.DetectAsync(FlutterResult(installed: false, sdkRoot: null));

        Assert.Equal(DartSdkDetectionStatus.MetadataMissing, result.Status);
        var candidate = Assert.Single(result.Candidates);
        Assert.Null(candidate.Version);
        Assert.Contains("metadata file is missing", candidate.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DetectAsync_EmptyVersionFile_ReturnsMetadataInvalidAndPreservesRawEvidence()
    {
        using var fixture = new DartFixture();
        var standalone = fixture.CreateStandaloneDart("empty", string.Empty);
        var detector = new DartSdkDetector(new StubPathDiscovery(PathResult(standalone.ExecutablePath)));

        var result = await detector.DetectAsync(FlutterResult(installed: false, sdkRoot: null));

        Assert.Equal(DartSdkDetectionStatus.MetadataInvalid, result.Status);
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(string.Empty, candidate.RawVersionMetadata);
        Assert.Contains("empty", candidate.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DetectAsync_PreCancelled_ReturnsCancelledWithoutPathDiscovery()
    {
        var discovery = new StubPathDiscovery(PathResult());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await new DartSdkDetector(discovery).DetectAsync(
            FlutterResult(installed: false, sdkRoot: null),
            cancellationToken: cancellation.Token);

        Assert.Equal(DartSdkDetectionStatus.Cancelled, result.Status);
        Assert.Equal(0, discovery.CallCount);
    }

    private static FlutterDetectionResult FlutterResult(string sdkRoot)
        => FlutterResult(installed: true, sdkRoot);

    private static FlutterDetectionResult FlutterResult(bool installed, string? sdkRoot)
        => new(
            installed ? FlutterSdkDetectionStatus.Succeeded : FlutterSdkDetectionStatus.Missing,
            installed,
            installed && sdkRoot is not null ? Path.Combine(sdkRoot, "bin", "flutter.bat") : null,
            sdkRoot,
            installed ? "3.35.0" : null,
            installed ? "stable" : null,
            Array.Empty<FlutterSdkCandidate>(),
            HasConflict: false,
            Message: installed ? "ready" : "missing");

    private static PathExecutableDiscoveryResult PathResult(params string[] executablePaths)
    {
        var matches = executablePaths.Select((path, index) => new PathExecutableMatch(
            Path.GetFullPath(path),
            Path.GetDirectoryName(Path.GetFullPath(path))!,
            Path.GetFileName(path),
            Path.GetExtension(path),
            PathIndex: index,
            ResolutionOrder: index,
            IsPreferred: index == 0,
            IsShadowed: index > 0)).ToArray();
        return new PathExecutableDiscoveryResult(
            PathExecutableDiscoveryStatus.Succeeded,
            "dart",
            matches,
            matches.Select(match => match.DirectoryPath).ToArray(),
            new[] { ".EXE", ".BAT" },
            Array.Empty<IgnoredPathEntry>(),
            matches.Length == 0 ? "not found" : "found");
    }

    private sealed class StubPathDiscovery : IPathExecutableDiscovery
    {
        private readonly PathExecutableDiscoveryResult _result;

        public StubPathDiscovery(PathExecutableDiscoveryResult result) => _result = result;

        public int CallCount { get; private set; }

        public PathExecutableDiscoveryResult Discover(PathExecutableDiscoveryRequest request)
        {
            CallCount++;
            Assert.Equal("dart", request.ExecutableName);
            return _result;
        }
    }

    private sealed class DartFixture : IDisposable
    {
        public DartFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "FlutterBuildDoctorTests", "DartSdk", Guid.NewGuid().ToString("N"));
            FlutterSdkRoot = Path.Combine(Root, "flutter");
            Directory.CreateDirectory(Path.Combine(FlutterSdkRoot, "bin"));
        }

        public string Root { get; }
        public string FlutterSdkRoot { get; }

        public DartPaths CreateBundledDart(string? version)
        {
            var sdkRoot = Path.Combine(FlutterSdkRoot, "bin", "cache", "dart-sdk");
            return CreateDartAtSdkRoot(sdkRoot, version);
        }

        public DartPaths CreateStandaloneDart(string name, string? version)
        {
            var sdkRoot = Path.Combine(Root, name);
            return CreateDartAtSdkRoot(sdkRoot, version);
        }

        private static DartPaths CreateDartAtSdkRoot(string sdkRoot, string? version)
        {
            var bin = Path.Combine(sdkRoot, "bin");
            Directory.CreateDirectory(bin);
            var executable = Path.Combine(bin, "dart.exe");
            File.WriteAllText(executable, "fixture");
            if (version is not null)
                File.WriteAllText(Path.Combine(sdkRoot, "version"), version);
            return new DartPaths(sdkRoot, executable);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                    Directory.Delete(Root, recursive: true);
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }

    private sealed record DartPaths(string SdkRoot, string ExecutablePath);
}
