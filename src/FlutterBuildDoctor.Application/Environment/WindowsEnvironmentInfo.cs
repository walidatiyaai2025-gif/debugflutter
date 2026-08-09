namespace FlutterBuildDoctor.Application.Environment;

public enum WindowsEnvironmentDetectionStatus
{
    Succeeded = 0,
    NotWindows,
    Unavailable
}

public sealed record WindowsEnvironmentInfo(
    WindowsEnvironmentDetectionStatus Status,
    string? Description,
    string? Version,
    int? MajorVersion,
    int? MinorVersion,
    int? BuildNumber,
    string? OsArchitecture,
    string? ProcessArchitecture,
    bool Is64BitOperatingSystem,
    bool Is64BitProcess,
    string Message)
{
    public bool IsSuccess => Status == WindowsEnvironmentDetectionStatus.Succeeded;
}

public interface IWindowsEnvironmentDetector
{
    WindowsEnvironmentInfo Detect();
}
