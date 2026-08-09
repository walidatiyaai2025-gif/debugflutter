using System.Runtime.InteropServices;

namespace FlutterBuildDoctor.Infrastructure.Environment;

public sealed record WindowsRuntimeInfo(
    bool IsWindows,
    string Description,
    Version Version,
    Architecture OsArchitecture,
    Architecture ProcessArchitecture,
    bool Is64BitOperatingSystem,
    bool Is64BitProcess);

public interface IWindowsRuntimeInfoSource
{
    WindowsRuntimeInfo Read();
}

public sealed class SystemWindowsRuntimeInfoSource : IWindowsRuntimeInfoSource
{
    public WindowsRuntimeInfo Read()
        => new(
            OperatingSystem.IsWindows(),
            RuntimeInformation.OSDescription,
            System.Environment.OSVersion.Version,
            RuntimeInformation.OSArchitecture,
            RuntimeInformation.ProcessArchitecture,
            System.Environment.Is64BitOperatingSystem,
            System.Environment.Is64BitProcess);
}
