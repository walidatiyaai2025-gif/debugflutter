using System.Runtime.InteropServices;
using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Infrastructure.Environment;

namespace FlutterBuildDoctor.UnitTests.EnvironmentDiscovery;

public sealed class WindowsEnvironmentDetectorTests
{
    [Fact]
    public void Detect_WindowsRuntime_ReturnsVersionBuildAndArchitecture()
    {
        var source = new StubSource(new WindowsRuntimeInfo(
            IsWindows: true,
            Description: "Microsoft Windows 11 Pro",
            Version: new Version(10, 0, 26100, 1234),
            OsArchitecture: Architecture.X64,
            ProcessArchitecture: Architecture.X64,
            Is64BitOperatingSystem: true,
            Is64BitProcess: true));

        var result = new WindowsEnvironmentDetector(source).Detect();

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(WindowsEnvironmentDetectionStatus.Succeeded, result.Status);
        Assert.Equal("Microsoft Windows 11 Pro", result.Description);
        Assert.Equal("10.0.26100.1234", result.Version);
        Assert.Equal(10, result.MajorVersion);
        Assert.Equal(0, result.MinorVersion);
        Assert.Equal(26100, result.BuildNumber);
        Assert.Equal("X64", result.OsArchitecture);
        Assert.Equal("X64", result.ProcessArchitecture);
        Assert.True(result.Is64BitOperatingSystem);
        Assert.True(result.Is64BitProcess);
    }

    [Fact]
    public void Detect_Arm64OsWithX64Process_PreservesBothArchitectures()
    {
        var source = new StubSource(new WindowsRuntimeInfo(
            IsWindows: true,
            Description: "Microsoft Windows",
            Version: new Version(10, 0, 26100),
            OsArchitecture: Architecture.Arm64,
            ProcessArchitecture: Architecture.X64,
            Is64BitOperatingSystem: true,
            Is64BitProcess: true));

        var result = new WindowsEnvironmentDetector(source).Detect();

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal("Arm64", result.OsArchitecture);
        Assert.Equal("X64", result.ProcessArchitecture);
        Assert.Contains("process architecture is X64", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Detect_NonWindowsRuntime_ReturnsNotWindowsWithEvidence()
    {
        var source = new StubSource(new WindowsRuntimeInfo(
            IsWindows: false,
            Description: "Linux 6.8",
            Version: new Version(6, 8),
            OsArchitecture: Architecture.X64,
            ProcessArchitecture: Architecture.X64,
            Is64BitOperatingSystem: true,
            Is64BitProcess: true));

        var result = new WindowsEnvironmentDetector(source).Detect();

        Assert.Equal(WindowsEnvironmentDetectionStatus.NotWindows, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Equal("Linux 6.8", result.Description);
        Assert.Equal("6.8", result.Version);
        Assert.Null(result.BuildNumber);
        Assert.Contains("not Windows", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Detect_SourceFailure_ReturnsUnavailableWithoutThrowing()
    {
        var result = new WindowsEnvironmentDetector(new ThrowingSource()).Detect();

        Assert.Equal(WindowsEnvironmentDetectionStatus.Unavailable, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Version);
        Assert.Contains("runtime information could not be read", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fixture failure", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubSource : IWindowsRuntimeInfoSource
    {
        private readonly WindowsRuntimeInfo _value;

        public StubSource(WindowsRuntimeInfo value)
        {
            _value = value;
        }

        public WindowsRuntimeInfo Read() => _value;
    }

    private sealed class ThrowingSource : IWindowsRuntimeInfoSource
    {
        public WindowsRuntimeInfo Read() => throw new InvalidOperationException("fixture failure");
    }
}
