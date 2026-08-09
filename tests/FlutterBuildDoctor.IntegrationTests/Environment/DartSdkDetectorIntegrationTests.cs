using FlutterBuildDoctor.Flutter.Detection;
using FlutterBuildDoctor.Infrastructure.Environment;

namespace FlutterBuildDoctor.IntegrationTests.Environment;

public sealed class DartSdkDetectorIntegrationTests
{
    [Fact]
    public async Task DetectAsync_ComposesWithWindowsPathDiscoveryAndFlutterBundledDart()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlutterBuildDoctorTests", "DartIntegration", Guid.NewGuid().ToString("N"));
        try
        {
            var flutterRoot = Path.Combine(root, "flutter");
            Directory.CreateDirectory(Path.Combine(flutterRoot, "bin"));
            var dartRoot = Path.Combine(flutterRoot, "bin", "cache", "dart-sdk");
            var dartBin = Path.Combine(dartRoot, "bin");
            Directory.CreateDirectory(dartBin);
            var dartPath = Path.Combine(dartBin, "dart.exe");
            File.WriteAllText(dartPath, "fixture");
            File.WriteAllText(Path.Combine(dartRoot, "version"), "3.9.3");

            var flutter = new FlutterDetectionResult(
                FlutterSdkDetectionStatus.Succeeded,
                Installed: true,
                FlutterPath: Path.Combine(flutterRoot, "bin", "flutter.bat"),
                FlutterSdkPath: flutterRoot,
                FlutterVersion: "3.35.0",
                Channel: "stable",
                Candidates: Array.Empty<FlutterSdkCandidate>(),
                HasConflict: false,
                Message: "ready");
            var detector = new DartSdkDetector(new WindowsPathExecutableDiscovery());

            var result = await detector.DetectAsync(
                flutter,
                new DartSdkDetectionRequest(
                    PathValue: dartBin,
                    PathExtValue: ".EXE;.BAT"));

            Assert.True(result.IsSuccess, result.Message);
            Assert.Equal("3.9.3", result.FlutterBundledCandidate!.Version);
            Assert.Equal(dartPath, result.FlutterBundledCandidate.ExecutablePath, ignoreCase: true);
            Assert.True(result.FlutterBundledCandidate.IsPathPreferred);
            Assert.False(result.HasFlutterPathMismatch);
            Assert.NotNull(result.PathDiscovery);
            Assert.True(result.PathDiscovery!.IsSuccess);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }
}
