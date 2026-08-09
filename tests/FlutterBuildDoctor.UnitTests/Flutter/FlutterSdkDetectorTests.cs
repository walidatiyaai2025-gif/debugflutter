using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Flutter.Detection;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class FlutterSdkDetectorTests
{
    [Fact]
    public async Task DetectAsync_CachedVersionJson_ReturnsPreferredSdkVersionAndChannel()
    {
        using var fixture = new FlutterSdkFixture();
        var preferred = fixture.CreateSdk(
            "preferred",
            "{\"frameworkVersion\":\"3.35.1\",\"channel\":\"stable\"}");
        var discovery = new StubPathDiscovery(ResultFor(preferred));
        var detector = new FlutterSdkDetector(discovery);

        var result = await detector.DetectAsync();

        Assert.True(result.IsSuccess, result.Message);
        Assert.True(result.Installed);
        Assert.Equal(preferred.ExecutablePath, result.FlutterPath, ignoreCase: true);
        Assert.Equal(preferred.SdkRoot, result.FlutterSdkPath, ignoreCase: true);
        Assert.Equal("3.35.1", result.FlutterVersion);
        Assert.Equal("stable", result.Channel);
        Assert.Equal(FlutterVersionMetadataSource.CachedVersionJson, result.MetadataSource);
        Assert.False(result.HasConflict);
        Assert.Single(result.Candidates);
        Assert.Contains("frameworkVersion", result.RawMetadata, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetectAsync_MultiplePathMatches_PreservesConflictAndOnlyReadsPreferredMetadata()
    {
        using var fixture = new FlutterSdkFixture();
        var preferred = fixture.CreateSdk(
            "first",
            "{\"frameworkVersion\":\"3.35.1\",\"channel\":\"stable\"}");
        var shadowed = fixture.CreateSdk(
            "second",
            "{\"frameworkVersion\":\"3.29.0\",\"channel\":\"beta\"}");
        var discovery = new StubPathDiscovery(ResultFor(preferred, shadowed));
        var detector = new FlutterSdkDetector(discovery);

        var result = await detector.DetectAsync();

        Assert.True(result.IsSuccess, result.Message);
        Assert.True(result.HasConflict);
        Assert.Equal(2, result.Candidates.Count);
        Assert.True(result.Candidates[0].IsPreferred);
        Assert.True(result.Candidates[1].IsShadowed);
        Assert.Equal(preferred.ExecutablePath, result.FlutterPath, ignoreCase: true);
        Assert.Equal("3.35.1", result.FlutterVersion);
        Assert.Equal("stable", result.Channel);
        Assert.Contains("shadowed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DetectAsync_LegacyVersionAndGitHead_FallsBackWithoutRunningFlutter()
    {
        using var fixture = new FlutterSdkFixture();
        var sdk = fixture.CreateLegacySdk("legacy", "3.24.5", "stable");
        var detector = new FlutterSdkDetector(new StubPathDiscovery(ResultFor(sdk)));

        var result = await detector.DetectAsync();

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal("3.24.5", result.FlutterVersion);
        Assert.Equal("stable", result.Channel);
        Assert.Equal(FlutterVersionMetadataSource.LegacyVersionAndGitHead, result.MetadataSource);
        Assert.Contains("refs/heads/stable", result.RawMetadata, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetectAsync_MissingFlutter_ReturnsMissingWithoutCandidate()
    {
        var pathResult = new PathExecutableDiscoveryResult(
            PathExecutableDiscoveryStatus.Succeeded,
            "flutter",
            Array.Empty<PathExecutableMatch>(),
            Array.Empty<string>(),
            new[] { ".BAT", ".EXE" },
            Array.Empty<IgnoredPathEntry>(),
            "not found");
        var detector = new FlutterSdkDetector(new StubPathDiscovery(pathResult));

        var result = await detector.DetectAsync();

        Assert.Equal(FlutterSdkDetectionStatus.Missing, result.Status);
        Assert.False(result.Installed);
        Assert.Null(result.FlutterPath);
        Assert.Null(result.FlutterSdkPath);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task DetectAsync_PreferredPathOutsideSdkBin_ReturnsInvalidLayout()
    {
        using var fixture = new FlutterSdkFixture();
        var shimDirectory = fixture.CreateDirectory("shim");
        var executablePath = Path.Combine(shimDirectory, "flutter.bat");
        File.WriteAllText(executablePath, "shim");
        var match = new PathExecutableMatch(
            executablePath,
            shimDirectory,
            "flutter.bat",
            ".bat",
            0,
            0,
            IsPreferred: true,
            IsShadowed: false);
        var detector = new FlutterSdkDetector(new StubPathDiscovery(new PathExecutableDiscoveryResult(
            PathExecutableDiscoveryStatus.Succeeded,
            "flutter",
            new[] { match },
            new[] { shimDirectory },
            new[] { ".BAT" },
            Array.Empty<IgnoredPathEntry>(),
            "found")));

        var result = await detector.DetectAsync();

        Assert.Equal(FlutterSdkDetectionStatus.InvalidSdkLayout, result.Status);
        Assert.True(result.Installed);
        Assert.Equal(executablePath, result.FlutterPath, ignoreCase: true);
        Assert.Null(result.FlutterVersion);
        Assert.False(result.Candidates[0].HasExpectedSdkLayout);
    }

    [Fact]
    public async Task DetectAsync_MalformedCachedMetadata_PreservesRawEvidence()
    {
        using var fixture = new FlutterSdkFixture();
        var sdk = fixture.CreateSdk("broken", "{ definitely-not-json }");
        var detector = new FlutterSdkDetector(new StubPathDiscovery(ResultFor(sdk)));

        var result = await detector.DetectAsync();

        Assert.Equal(FlutterSdkDetectionStatus.MetadataInvalid, result.Status);
        Assert.True(result.Installed);
        Assert.Equal(FlutterVersionMetadataSource.CachedVersionJson, result.MetadataSource);
        Assert.Equal("{ definitely-not-json }", result.RawMetadata);
        Assert.Contains("could not be parsed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DetectAsync_CachedMetadataMissingRequiredChannel_ReturnsMetadataInvalid()
    {
        using var fixture = new FlutterSdkFixture();
        var sdk = fixture.CreateSdk("broken-channel", "{\"frameworkVersion\":\"3.35.1\"}");
        var detector = new FlutterSdkDetector(new StubPathDiscovery(ResultFor(sdk)));

        var result = await detector.DetectAsync();

        Assert.Equal(FlutterSdkDetectionStatus.MetadataInvalid, result.Status);
        Assert.Contains("channel", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("frameworkVersion", result.RawMetadata, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetectAsync_ExpectedSdkWithoutVersionMetadata_ReturnsMetadataMissing()
    {
        using var fixture = new FlutterSdkFixture();
        var sdk = fixture.CreateSdkWithoutMetadata("no-metadata");
        var detector = new FlutterSdkDetector(new StubPathDiscovery(ResultFor(sdk)));

        var result = await detector.DetectAsync();

        Assert.Equal(FlutterSdkDetectionStatus.MetadataMissing, result.Status);
        Assert.True(result.Installed);
        Assert.Equal(sdk.SdkRoot, result.FlutterSdkPath, ignoreCase: true);
        Assert.Null(result.FlutterVersion);
        Assert.Null(result.Channel);
    }

    [Fact]
    public async Task DetectAsync_CancelledBeforeDiscovery_ReturnsCancelledAndDoesNotDiscoverPath()
    {
        var discovery = new StubPathDiscovery(ResultFor(), throwIfCalled: true);
        var detector = new FlutterSdkDetector(discovery);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await detector.DetectAsync(cancellationToken: cancellation.Token);

        Assert.Equal(FlutterSdkDetectionStatus.Cancelled, result.Status);
        Assert.Equal(0, discovery.CallCount);
    }

    private static PathExecutableDiscoveryResult ResultFor(params SdkLayout[] sdks)
    {
        var matches = sdks
            .Select((sdk, index) => new PathExecutableMatch(
                sdk.ExecutablePath,
                Path.GetDirectoryName(sdk.ExecutablePath)!,
                Path.GetFileName(sdk.ExecutablePath),
                Path.GetExtension(sdk.ExecutablePath),
                index,
                index,
                IsPreferred: index == 0,
                IsShadowed: index > 0))
            .ToArray();

        return new PathExecutableDiscoveryResult(
            PathExecutableDiscoveryStatus.Succeeded,
            "flutter",
            matches,
            sdks.Select(sdk => Path.GetDirectoryName(sdk.ExecutablePath)!).ToArray(),
            new[] { ".BAT" },
            Array.Empty<IgnoredPathEntry>(),
            matches.Length == 0 ? "not found" : "found");
    }

    private sealed class StubPathDiscovery : IPathExecutableDiscovery
    {
        private readonly PathExecutableDiscoveryResult _result;
        private readonly bool _throwIfCalled;

        public StubPathDiscovery(PathExecutableDiscoveryResult result, bool throwIfCalled = false)
        {
            _result = result;
            _throwIfCalled = throwIfCalled;
        }

        public int CallCount { get; private set; }

        public PathExecutableDiscoveryResult Discover(PathExecutableDiscoveryRequest request)
        {
            CallCount++;
            if (_throwIfCalled)
            {
                throw new InvalidOperationException("PATH discovery must not run after pre-cancellation.");
            }

            Assert.Equal("flutter", request.ExecutableName);
            return _result;
        }
    }

    private sealed record SdkLayout(string SdkRoot, string ExecutablePath);

    private sealed class FlutterSdkFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "FlutterBuildDoctorTests",
            Guid.NewGuid().ToString("N"));

        public FlutterSdkFixture()
        {
            Directory.CreateDirectory(_root);
        }

        public SdkLayout CreateSdk(string name, string metadataJson)
        {
            var sdk = CreateSdkWithoutMetadata(name);
            var metadataPath = Path.Combine(sdk.SdkRoot, "bin", "cache", "flutter.version.json");
            File.WriteAllText(metadataPath, metadataJson);
            return sdk;
        }

        public SdkLayout CreateLegacySdk(string name, string version, string channel)
        {
            var sdk = CreateSdkWithoutMetadata(name);
            Directory.CreateDirectory(Path.Combine(sdk.SdkRoot, ".git"));
            File.WriteAllText(Path.Combine(sdk.SdkRoot, "version"), version + Environment.NewLine);
            File.WriteAllText(
                Path.Combine(sdk.SdkRoot, ".git", "HEAD"),
                $"ref: refs/heads/{channel}{Environment.NewLine}");
            return sdk;
        }

        public SdkLayout CreateSdkWithoutMetadata(string name)
        {
            var sdkRoot = Path.Combine(_root, name);
            var bin = Path.Combine(sdkRoot, "bin");
            Directory.CreateDirectory(Path.Combine(bin, "cache"));
            var executable = Path.Combine(bin, "flutter.bat");
            File.WriteAllText(executable, "@echo off");
            return new SdkLayout(sdkRoot, executable);
        }

        public string CreateDirectory(string name)
        {
            var path = Path.Combine(_root, name);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
