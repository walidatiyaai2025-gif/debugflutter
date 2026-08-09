namespace FlutterBuildDoctor.Android.Detection;

public sealed class AndroidCommandLineToolsDetector : IAndroidCommandLineToolsDetector
{
    private const string RevisionProperty = "Pkg.Revision";

    public AndroidCommandLineToolsDetectionResult Detect(AndroidSdkRootDetectionResult sdkRootResult)
    {
        ArgumentNullException.ThrowIfNull(sdkRootResult);

        var effectiveRoot = sdkRootResult.EffectiveCandidate;
        var sdkRoot = effectiveRoot?.NormalizedPath ?? string.Empty;
        if (!sdkRootResult.IsSuccess || effectiveRoot is null || !effectiveRoot.IsValid)
        {
            return new AndroidCommandLineToolsDetectionResult(
                AndroidCommandLineToolsDetectionStatus.AndroidSdkRootUnavailable,
                sdkRoot,
                EffectiveCandidate: null,
                Candidates: Array.Empty<AndroidCommandLineToolsCandidate>(),
                HasMultipleInstallations: false,
                Message: "A validated effective Android SDK root is required before command-line tools can be detected.");
        }

        IReadOnlyList<AndroidCommandLineToolsCandidate> candidates;
        try
        {
            candidates = DiscoverCandidates(sdkRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new AndroidCommandLineToolsDetectionResult(
                AndroidCommandLineToolsDetectionStatus.MetadataInvalid,
                sdkRoot,
                EffectiveCandidate: null,
                Candidates: Array.Empty<AndroidCommandLineToolsCandidate>(),
                HasMultipleInstallations: false,
                Message: $"Android command-line tools could not be inspected: {ex.Message}");
        }

        if (candidates.Count == 0)
        {
            return new AndroidCommandLineToolsDetectionResult(
                AndroidCommandLineToolsDetectionStatus.CommandLineToolsMissing,
                sdkRoot,
                EffectiveCandidate: null,
                Candidates: candidates,
                HasMultipleInstallations: false,
                Message: $"No Android command-line tools installations were found under '{sdkRoot}'.");
        }

        var effective = SelectEffectiveCandidate(candidates);
        var projected = candidates
            .Select(candidate => candidate with
            {
                IsEffective = ReferenceEquals(candidate, effective)
            })
            .ToArray();
        effective = projected.Single(candidate => candidate.IsEffective);

        if (!effective.SdkManagerExists)
        {
            var suffix = projected.Length > 1
                ? " Other installations are preserved as evidence and were not promoted automatically."
                : string.Empty;
            return new AndroidCommandLineToolsDetectionResult(
                AndroidCommandLineToolsDetectionStatus.EffectiveSdkManagerMissing,
                sdkRoot,
                effective,
                projected,
                projected.Length > 1,
                $"The effective Android command-line tools installation at '{effective.InstallationPath}' does not contain sdkmanager.{suffix}");
        }

        if (string.IsNullOrWhiteSpace(effective.Revision))
        {
            return new AndroidCommandLineToolsDetectionResult(
                AndroidCommandLineToolsDetectionStatus.MetadataInvalid,
                sdkRoot,
                effective,
                projected,
                projected.Length > 1,
                $"sdkmanager was found at '{effective.SdkManagerPath}', but its command-line tools revision could not be read from source.properties.");
        }

        var conflictSuffix = projected.Length > 1
            ? $" {projected.Length - 1} additional command-line tools installation(s) were also found."
            : string.Empty;
        return new AndroidCommandLineToolsDetectionResult(
            AndroidCommandLineToolsDetectionStatus.Succeeded,
            sdkRoot,
            effective,
            projected,
            projected.Length > 1,
            $"Android command-line tools {effective.Revision} detected at '{effective.InstallationPath}'.{conflictSuffix}");
    }

    private static IReadOnlyList<AndroidCommandLineToolsCandidate> DiscoverCandidates(string sdkRoot)
    {
        var candidates = new List<AndroidCommandLineToolsCandidate>();
        var commandLineToolsRoot = Path.Combine(sdkRoot, "cmdline-tools");
        if (Directory.Exists(commandLineToolsRoot))
        {
            foreach (var installationPath in Directory
                         .EnumerateDirectories(commandLineToolsRoot)
                         .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
            {
                var directoryName = Path.GetFileName(installationPath);
                var layout = string.Equals(directoryName, "latest", StringComparison.OrdinalIgnoreCase)
                    ? AndroidCommandLineToolsLayout.LatestAlias
                    : AndroidCommandLineToolsLayout.Versioned;
                candidates.Add(BuildCandidate(installationPath, layout));
            }
        }

        var legacyPath = Path.Combine(sdkRoot, "tools");
        var legacySdkManager = FindSdkManager(legacyPath);
        if (Directory.Exists(legacyPath) && legacySdkManager is not null)
            candidates.Add(BuildCandidate(legacyPath, AndroidCommandLineToolsLayout.LegacyTools));

        return candidates;
    }

    private static AndroidCommandLineToolsCandidate BuildCandidate(
        string installationPath,
        AndroidCommandLineToolsLayout layout)
    {
        var sourcePropertiesPath = Path.Combine(installationPath, "source.properties");
        var sdkManagerPath = FindSdkManager(installationPath);
        string? raw = null;
        string? revision = null;
        string? message = null;

        if (File.Exists(sourcePropertiesPath))
        {
            try
            {
                raw = File.ReadAllText(sourcePropertiesPath);
                revision = ParseProperty(raw, RevisionProperty);
                if (string.IsNullOrWhiteSpace(revision))
                    message = $"'{RevisionProperty}' is missing from source.properties.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                message = $"source.properties could not be read: {ex.Message}";
            }
        }
        else
        {
            message = "source.properties is missing.";
        }

        if (sdkManagerPath is null)
        {
            message = string.IsNullOrWhiteSpace(message)
                ? "sdkmanager was not found under the installation bin directory."
                : $"{message} sdkmanager was not found under the installation bin directory.";
        }

        return new AndroidCommandLineToolsCandidate(
            Path.GetFullPath(installationPath),
            sdkManagerPath,
            revision,
            layout,
            IsEffective: false,
            SdkManagerExists: sdkManagerPath is not null,
            SourcePropertiesPath: File.Exists(sourcePropertiesPath) ? sourcePropertiesPath : null,
            RawSourceProperties: raw,
            Message: message);
    }

    private static string? FindSdkManager(string installationPath)
    {
        var bin = Path.Combine(installationPath, "bin");
        foreach (var fileName in new[] { "sdkmanager.bat", "sdkmanager.exe" })
        {
            var candidate = Path.Combine(bin, fileName);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        return null;
    }

    private static string? ParseProperty(string raw, string propertyName)
    {
        foreach (var line in raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            var separator = trimmed.IndexOf('=');
            if (separator <= 0)
                continue;

            var key = trimmed[..separator].Trim();
            if (!string.Equals(key, propertyName, StringComparison.OrdinalIgnoreCase))
                continue;

            var value = trimmed[(separator + 1)..].Trim();
            return value.Length == 0 ? null : value;
        }

        return null;
    }

    private static AndroidCommandLineToolsCandidate SelectEffectiveCandidate(
        IReadOnlyList<AndroidCommandLineToolsCandidate> candidates)
    {
        var latest = candidates.FirstOrDefault(candidate =>
            candidate.Layout == AndroidCommandLineToolsLayout.LatestAlias);
        if (latest is not null)
            return latest;

        var versioned = candidates
            .Where(candidate => candidate.Layout == AndroidCommandLineToolsLayout.Versioned)
            .OrderByDescending(candidate => ParseRevision(candidate.Revision))
            .ThenByDescending(candidate => candidate.Revision, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (versioned is not null)
            return versioned;

        return candidates.First();
    }

    private static Version ParseRevision(string? revision)
    {
        if (Version.TryParse(revision, out var parsed))
            return parsed;

        return new Version(0, 0);
    }
}
