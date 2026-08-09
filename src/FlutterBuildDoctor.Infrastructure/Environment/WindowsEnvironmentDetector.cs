using FlutterBuildDoctor.Application.Environment;

namespace FlutterBuildDoctor.Infrastructure.Environment;

public sealed class WindowsEnvironmentDetector : IWindowsEnvironmentDetector
{
    private readonly IWindowsRuntimeInfoSource _source;

    public WindowsEnvironmentDetector(IWindowsRuntimeInfoSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public WindowsEnvironmentInfo Detect()
    {
        WindowsRuntimeInfo runtime;
        try
        {
            runtime = _source.Read();
        }
        catch (Exception ex)
        {
            return new WindowsEnvironmentInfo(
                WindowsEnvironmentDetectionStatus.Unavailable,
                Description: null,
                Version: null,
                MajorVersion: null,
                MinorVersion: null,
                BuildNumber: null,
                OsArchitecture: null,
                ProcessArchitecture: null,
                Is64BitOperatingSystem: false,
                Is64BitProcess: false,
                Message: $"Windows runtime information could not be read: {ex.Message}");
        }

        if (!runtime.IsWindows)
        {
            return new WindowsEnvironmentInfo(
                WindowsEnvironmentDetectionStatus.NotWindows,
                runtime.Description,
                runtime.Version.ToString(),
                runtime.Version.Major,
                runtime.Version.Minor,
                runtime.Version.Build >= 0 ? runtime.Version.Build : null,
                runtime.OsArchitecture.ToString(),
                runtime.ProcessArchitecture.ToString(),
                runtime.Is64BitOperatingSystem,
                runtime.Is64BitProcess,
                Message: $"Current operating system is not Windows: {runtime.Description}.");
        }

        var buildNumber = runtime.Version.Build >= 0 ? runtime.Version.Build : (int?)null;
        return new WindowsEnvironmentInfo(
            WindowsEnvironmentDetectionStatus.Succeeded,
            runtime.Description,
            runtime.Version.ToString(),
            runtime.Version.Major,
            runtime.Version.Minor,
            buildNumber,
            runtime.OsArchitecture.ToString(),
            runtime.ProcessArchitecture.ToString(),
            runtime.Is64BitOperatingSystem,
            runtime.Is64BitProcess,
            Message: $"Windows {runtime.Version} ({runtime.OsArchitecture}) detected; process architecture is {runtime.ProcessArchitecture}.");
    }
}
