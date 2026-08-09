using System.Runtime.InteropServices;
using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Infrastructure.Environment;

namespace FlutterBuildDoctor.IntegrationTests.Environment;

public sealed class WindowsEnvironmentDetectorIntegrationTests
{
    [Fact]
    public void Detect_ActualWindowsRunner_ReturnsCurrentRuntimeEvidence()
    {
        var detector = new WindowsEnvironmentDetector(new SystemWindowsRuntimeInfoSource());

        var result = detector.Detect();

        Assert.True(OperatingSystem.IsWindows());
        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(WindowsEnvironmentDetectionStatus.Succeeded, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Description));
        Assert.False(string.IsNullOrWhiteSpace(result.Version));
        Assert.Equal(System.Environment.OSVersion.Version.Major, result.MajorVersion);
        Assert.Equal(System.Environment.OSVersion.Version.Minor, result.MinorVersion);
        Assert.Equal(System.Environment.OSVersion.Version.Build, result.BuildNumber);
        Assert.Equal(RuntimeInformation.OSArchitecture.ToString(), result.OsArchitecture);
        Assert.Equal(RuntimeInformation.ProcessArchitecture.ToString(), result.ProcessArchitecture);
        Assert.Equal(System.Environment.Is64BitOperatingSystem, result.Is64BitOperatingSystem);
        Assert.Equal(System.Environment.Is64BitProcess, result.Is64BitProcess);
    }
}
