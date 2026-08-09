using System.Runtime.InteropServices;
using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Infrastructure.Environment;

namespace FlutterBuildDoctor.IntegrationTests.Environment;

public sealed class WindowsEnvironmentDetectorIntegrationTests
{
    [Fact]
    public void Detect_ActualWindowsRunner_ReturnsCurrentRuntimeEvidence()
    {
        var result = new WindowsEnvironmentDetector(new SystemWindowsRuntimeInfoSource()).Detect();
        Assert.True(OperatingSystem.IsWindows());
        Assert.True(result.IsSuccess, result.Message);
        Assert.False(string.IsNullOrWhiteSpace(result.Description));
        Assert.False(string.IsNullOrWhiteSpace(result.Version));
        Assert.Equal(System.Environment.OSVersion.Version.Build, result.BuildNumber);
        Assert.Equal(RuntimeInformation.OSArchitecture.ToString(), result.OsArchitecture);
        Assert.Equal(RuntimeInformation.ProcessArchitecture.ToString(), result.ProcessArchitecture);
    }
}
