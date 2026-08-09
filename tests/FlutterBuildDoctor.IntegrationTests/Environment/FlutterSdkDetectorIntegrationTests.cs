using FlutterBuildDoctor.Flutter.Detection;
using FlutterBuildDoctor.Infrastructure.Environment;

namespace FlutterBuildDoctor.IntegrationTests.Environment;

public sealed class FlutterSdkDetectorIntegrationTests
{
    [Fact]
    public async Task DetectAsync_RealPathDiscoveryAndCachedMetadata_ResolvesFlutterSdk()
    {
        using var fixture = new FlutterSdkFixture();
        var sdk = fixture.CreateSdk(
            "flutter-stable",
            "{\"frameworkVersion\":\"3.35.1\",\"channel\":\"stable\",\"repositoryUrl\":\"https://github.com/flutter/flutter.git\"}");
        var detector = new FlutterSdkDetector(new WindowsPathExecutableDiscovery());

        var result = await detector.DetectAsync(new FlutterSdkDetectionRequest(
            Path.GetDirectoryName(sdk.ExecutablePath),
            ".BAT"));

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(sdk.ExecutablePath, result.FlutterPath, ignoreCase: true);
        Assert.Equal(sdk.SdkRoot, result.FlutterSdkPath, ignoreCase: true);
        Assert.Equal("3.35.1", result.FlutterVersion);
        Assert.Equal("stable", result.Channel);
        Assert.Equal(FlutterVersionMetadataSource.CachedVersionJson, result.MetadataSource);
        Assert.False(result.HasConflict);
    }

    [Fact]
    public async Task DetectAsync_TwoSdkBins_UsesPathOrderAndPreservesConflictEvidence()
    {
        using var fixture = new FlutterSdkFixture();
        var first = fixture.CreateSdk(
            "flutter-first",
            "{\"frameworkVersion\":\"3.35.1\",\"channel\":\"stable\"}");
        var second = fixture.CreateSdk(
            "flutter-second",
            "{\"frameworkVersion\":\"3.29.3\",\"channel\":\"beta\"}");
        var detector = new FlutterSdkDetector(new WindowsPathExecutableDiscovery());
        var path = string.Join(
            ';',
            Path.GetDirectoryName(first.ExecutablePath),
            Path.GetDirectoryName(second.ExecutablePath));

        var result = await detector.DetectAsync(new FlutterSdkDetectionRequest(path, ".BAT"));

        Assert.True(result.IsSuccess, result.Message);
        Assert.True(result.HasConflict);
        Assert.Equal(2, result.Candidates.Count);
        Assert.Equal(first.ExecutablePath, result.FlutterPath, ignoreCase: true);
        Assert.Equal("3.35.1", result.FlutterVersion);
        Assert.True(result.Candidates[0].IsPreferred);
        Assert.True(result.Candidates[1].IsShadowed);
        Assert.Equal(second.ExecutablePath, result.Candidates[1].ExecutablePath, ignoreCase: true);
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
            var sdkRoot = Path.Combine(_root, name);
            var bin = Path.Combine(sdkRoot, "bin");
            var cache = Path.Combine(bin, "cache");
            Directory.CreateDirectory(cache);
            var executable = Path.Combine(bin, "flutter.bat");
            File.WriteAllText(executable, "@echo off");
            File.WriteAllText(Path.Combine(cache, "flutter.version.json"), metadataJson);
            return new SdkLayout(sdkRoot, executable);
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
