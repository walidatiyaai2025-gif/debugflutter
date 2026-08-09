namespace FlutterBuildDoctor.Android.Detection;

public sealed class AndroidBuildToolsDetector : IAndroidBuildToolsDetector
{
    public AndroidBuildToolsDetectionResult Detect(AndroidSdkRootDetectionResult sdkRootResult)
    {
        ArgumentNullException.ThrowIfNull(sdkRootResult);

        var effectiveRoot = sdkRootResult.EffectiveCandidate;
        var sdkRoot = effectiveRoot?.NormalizedPath ?? string.Empty;
        if (!sdkRootResult.IsSuccess || effectiveRoot is null || !effectiveRoot.IsValid)
        {
            return new AndroidBuildToolsDetectionResult(
                AndroidBuildToolsDetectionStatus.AndroidSdkRootUnavailable,
                sdkRoot,
                Array.Empty<AndroidBuildToolsPackage>(),
                "A validated effective Android SDK root is required before build-tools can be enumerated.");
        }

        var buildToolsRoot = Path.Combine(sdkRoot, "build-tools");
        if (!Directory.Exists(buildToolsRoot))
        {
            return new AndroidBuildToolsDetectionResult(
                AndroidBuildToolsDetectionStatus.BuildToolsDirectoryMissing,
                sdkRoot,
                Array.Empty<AndroidBuildToolsPackage>(),
                $"Android build-tools directory was not found at '{buildToolsRoot}'.");
        }

        IReadOnlyList<AndroidBuildToolsPackage> packages;
        try
        {
            packages = Directory
                .EnumerateDirectories(buildToolsRoot)
                .Select(BuildPackage)
                .OrderByDescending(package => ParseVersion(package.Revision))
                .ThenByDescending(package => package.Revision, StringComparer.OrdinalIgnoreCase)
                .ThenBy(package => package.DirectoryName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new AndroidBuildToolsDetectionResult(
                AndroidBuildToolsDetectionStatus.InspectionFailed,
                sdkRoot,
                Array.Empty<AndroidBuildToolsPackage>(),
                $"Installed Android build-tools could not be inspected: {ex.Message}");
        }

        if (packages.Count == 0)
        {
            return new AndroidBuildToolsDetectionResult(
                AndroidBuildToolsDetectionStatus.NoBuildToolsInstalled,
                sdkRoot,
                packages,
                $"No Android build-tools packages were found under '{buildToolsRoot}'.");
        }

        var usableCount = packages.Count(package => package.IsUsable);
        if (usableCount == 0)
        {
            return new AndroidBuildToolsDetectionResult(
                AndroidBuildToolsDetectionStatus.PartialInstallationsOnly,
                sdkRoot,
                packages,
                "Android build-tools directories were found, but none contain a resolvable revision and all required core tools.");
        }

        var partialCount = packages.Count - usableCount;
        var suffix = partialCount > 0
            ? $" {partialCount} additional partial/broken build-tools installation(s) are preserved as evidence."
            : string.Empty;
        return new AndroidBuildToolsDetectionResult(
            AndroidBuildToolsDetectionStatus.Succeeded,
            sdkRoot,
            packages,
            $"Detected {usableCount} usable Android build-tools package(s).{suffix}");
    }

    private static AndroidBuildToolsPackage BuildPackage(string installationPath)
    {
        var directoryName = Path.GetFileName(installationPath);
        var sourcePropertiesPath = Path.Combine(installationPath, "source.properties");
        var raw = ReadSourceProperties(sourcePropertiesPath, out var readError);
        var metadataRevision = ParseProperty(raw, "Pkg.Revision");
        var directoryRevision = LooksLikeRevision(directoryName) ? directoryName : null;
        var revision = metadataRevision ?? directoryRevision;

        var aapt2 = File.Exists(Path.Combine(installationPath, "aapt2.exe")) ||
                    File.Exists(Path.Combine(installationPath, "aapt2"));
        var zipAlign = File.Exists(Path.Combine(installationPath, "zipalign.exe")) ||
                       File.Exists(Path.Combine(installationPath, "zipalign"));
        var d8 = File.Exists(Path.Combine(installationPath, "d8.bat")) ||
                 File.Exists(Path.Combine(installationPath, "d8"));
        var apkSigner = File.Exists(Path.Combine(installationPath, "apksigner.bat")) ||
                        File.Exists(Path.Combine(installationPath, "apksigner"));

        var messages = new List<string>();
        if (!string.IsNullOrWhiteSpace(readError))
            messages.Add(readError);
        else if (raw is null)
            messages.Add("source.properties is missing.");

        if (metadataRevision is null && directoryRevision is not null)
            messages.Add($"Build-tools revision was inferred from directory name '{directoryName}'.");
        else if (metadataRevision is not null && directoryRevision is not null &&
                 !string.Equals(metadataRevision, directoryRevision, StringComparison.OrdinalIgnoreCase))
            messages.Add($"Directory revision '{directoryRevision}' differs from source.properties revision '{metadataRevision}'; metadata value is retained.");
        else if (revision is null)
            messages.Add("Build-tools revision could not be resolved from source.properties or directory name.");

        if (!aapt2) messages.Add("aapt2 is missing.");
        if (!zipAlign) messages.Add("zipalign is missing.");
        if (!d8) messages.Add("d8 is missing.");
        if (!apkSigner) messages.Add("apksigner is missing.");

        return new AndroidBuildToolsPackage(
            directoryName,
            Path.GetFullPath(installationPath),
            revision,
            aapt2,
            zipAlign,
            d8,
            apkSigner,
            File.Exists(sourcePropertiesPath) ? sourcePropertiesPath : null,
            raw,
            messages.Count == 0 ? null : string.Join(" ", messages));
    }

    private static string? ReadSourceProperties(string path, out string? error)
    {
        error = null;
        if (!File.Exists(path))
            return null;

        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"source.properties could not be read: {ex.Message}";
            return null;
        }
    }

    private static string? ParseProperty(string? raw, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        foreach (var line in raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            var separator = trimmed.IndexOf('=');
            if (separator <= 0)
                continue;

            if (!string.Equals(trimmed[..separator].Trim(), propertyName, StringComparison.OrdinalIgnoreCase))
                continue;

            var value = trimmed[(separator + 1)..].Trim();
            return value.Length == 0 ? null : value;
        }

        return null;
    }

    private static bool LooksLikeRevision(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return char.IsDigit(value[0]) && value.All(character =>
            char.IsLetterOrDigit(character) || character is '.' or '-' or '_');
    }

    private static Version ParseVersion(string? value)
    {
        if (Version.TryParse(value, out var parsed))
            return parsed;

        if (!string.IsNullOrWhiteSpace(value))
        {
            var numericPrefix = new string(value.TakeWhile(character => char.IsDigit(character) || character == '.').ToArray());
            if (Version.TryParse(numericPrefix.TrimEnd('.'), out parsed))
                return parsed;
        }

        return new Version(0, 0);
    }
}
