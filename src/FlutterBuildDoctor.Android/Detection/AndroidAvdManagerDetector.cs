namespace FlutterBuildDoctor.Android.Detection;

public sealed class AndroidAvdManagerDetector : IAndroidAvdManagerDetector
{
    public AndroidAvdManagerDetectionResult Detect(AndroidCommandLineToolsDetectionResult commandLineToolsResult)
    {
        ArgumentNullException.ThrowIfNull(commandLineToolsResult);

        if (commandLineToolsResult.EffectiveCandidate is null || commandLineToolsResult.Candidates.Count == 0)
        {
            return new AndroidAvdManagerDetectionResult(
                AndroidAvdManagerDetectionStatus.CommandLineToolsUnavailable,
                commandLineToolsResult.AndroidSdkRoot,
                EffectiveCandidate: null,
                Candidates: Array.Empty<AndroidAvdManagerCandidate>(),
                HasMultipleInstallations: false,
                Message: "A discovered command-line tools installation is required before avdmanager can be detected.");
        }

        var candidates = commandLineToolsResult.Candidates
            .Select(BuildCandidate)
            .ToArray();
        var effective = candidates.Single(candidate => candidate.IsEffective);

        if (!effective.Exists)
        {
            var suffix = candidates.Length > 1
                ? " Other command-line tools installations are preserved as evidence and were not promoted automatically."
                : string.Empty;
            return new AndroidAvdManagerDetectionResult(
                AndroidAvdManagerDetectionStatus.AvdManagerMissing,
                commandLineToolsResult.AndroidSdkRoot,
                effective,
                candidates,
                candidates.Length > 1,
                $"The effective command-line tools installation at '{effective.InstallationPath}' does not contain avdmanager.{suffix}");
        }

        var conflictSuffix = candidates.Length > 1
            ? $" {candidates.Length - 1} additional command-line tools installation(s) were checked and preserved as evidence."
            : string.Empty;
        return new AndroidAvdManagerDetectionResult(
            AndroidAvdManagerDetectionStatus.Succeeded,
            commandLineToolsResult.AndroidSdkRoot,
            effective,
            candidates,
            candidates.Length > 1,
            $"avdmanager detected at '{effective.AvdManagerPath}' from command-line tools {effective.CommandLineToolsRevision ?? "unknown revision"}.{conflictSuffix}");
    }

    private static AndroidAvdManagerCandidate BuildCandidate(AndroidCommandLineToolsCandidate commandLineTools)
    {
        var path = FindAvdManager(commandLineTools.InstallationPath);
        var message = path is null
            ? "avdmanager was not found under the installation bin directory."
            : null;

        return new AndroidAvdManagerCandidate(
            commandLineTools.InstallationPath,
            path,
            commandLineTools.Revision,
            commandLineTools.Layout,
            commandLineTools.IsEffective,
            Exists: path is not null,
            Message: message);
    }

    private static string? FindAvdManager(string installationPath)
    {
        var bin = Path.Combine(installationPath, "bin");
        foreach (var fileName in new[] { "avdmanager.bat", "avdmanager.exe", "avdmanager" })
        {
            var candidate = Path.Combine(bin, fileName);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        return null;
    }
}
