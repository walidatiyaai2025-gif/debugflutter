using FlutterBuildDoctor.Android.Detection;
using FlutterBuildDoctor.Infrastructure.Environment;
using FlutterBuildDoctor.Infrastructure.Processes;

namespace FlutterBuildDoctor.IntegrationTests.Environment;

public sealed class JavaInstallationDetectorIntegrationTests
{
    [Fact]
    public async Task DetectAsync_OnWindowsRunner_DetectsEffectiveJavaAndMetadata()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var detector = new JavaInstallationDetector(
            new WindowsPathExecutableDiscovery(),
            new ProcessRunner(new DefaultProcessSecretRedactor()));

        var result = await detector.DetectAsync(new JavaDetectionRequest(
            ProbeTimeout: TimeSpan.FromSeconds(15)));

        Assert.True(result.IsSuccess, result.Message);
        Assert.NotNull(result.PreferredInstallation);
        Assert.False(string.IsNullOrWhiteSpace(result.PreferredInstallation!.ExecutablePath));
        Assert.False(string.IsNullOrWhiteSpace(result.PreferredInstallation.Version));
        Assert.False(string.IsNullOrWhiteSpace(result.PreferredInstallation.Vendor));
        Assert.False(string.IsNullOrWhiteSpace(result.PreferredInstallation.Architecture));
        Assert.NotNull(result.PreferredInstallation.ProbeResult);
        Assert.True(result.PreferredInstallation.ProbeResult!.IsSuccess, result.PreferredInstallation.Message);
    }
}
