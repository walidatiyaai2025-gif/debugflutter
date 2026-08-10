using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Flutter.Release;

public sealed class ReleaseOutputActionService : IReleaseOutputActionService
{
    private readonly IDetachedProcessLauncher _processLauncher;

    public ReleaseOutputActionService(IDetachedProcessLauncher processLauncher)
    {
        _processLauncher = processLauncher ?? throw new ArgumentNullException(nameof(processLauncher));
    }

    public ProcessLaunchResult OpenOutputDirectory(string artifactPath)
    {
        var fullPath = RequireArtifact(artifactPath);
        var directory = Path.GetDirectoryName(fullPath)!;
        return _processLauncher.Launch(new ProcessRequest(
            "explorer.exe",
            new[] { directory },
            DisplayName: "Open release output directory"));
    }

    public ProcessLaunchResult RevealArtifact(string artifactPath)
    {
        var fullPath = RequireArtifact(artifactPath);
        return _processLauncher.Launch(new ProcessRequest(
            "explorer.exe",
            new[] { "/select,", fullPath },
            DisplayName: "Reveal release artifact"));
    }

    private static string RequireArtifact(string artifactPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);
        if (artifactPath.Any(char.IsControl))
            throw new ArgumentException("Control characters are not allowed in artifact paths.", nameof(artifactPath));
        var fullPath = Path.GetFullPath(artifactPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Release artifact was not found.", fullPath);
        return fullPath;
    }
}
