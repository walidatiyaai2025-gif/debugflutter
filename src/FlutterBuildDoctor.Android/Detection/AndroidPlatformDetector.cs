namespace FlutterBuildDoctor.Android.Detection;

public sealed class AndroidPlatformDetector : IAndroidPlatformDetector
{
    public AndroidPlatformDetectionResult Detect(AndroidSdkRootDetectionResult sdkRootResult)
    {
        ArgumentNullException.ThrowIfNull(sdkRootResult);

        var effectiveRoot = sdkRootResult.EffectiveCandidate;
        var sdkRoot = effectiveRoot?.NormalizedPath ?? string.Empty;
        if (!sdkRootResult.IsSuccess || effectiveRoot is null || !effectiveRoot.IsValid)
        {
            return new AndroidPlatformDetectionResult(
                AndroidPlatformDetectionStatus.AndroidSdkRootUnavailable,
                sdkRoot,
                Array.Empty<AndroidPlatformPackage>(),
                "A validated effective Android SDK root is required before installed platforms can be enumerated.");
        }

        var platformsPath = Path.Combine(sdkRoot, "platforms");
        if (!Directory.Exists(platformsPath))
        {
            return new AndroidPlatformDetectionResult(
                AndroidPlatformDetectionStatus.PlatformsDirectoryMissing,
                sdkRoot,
                Array.Empty<AndroidPlatformPackage>(),
                $"Android platforms directory was not found at '{platformsPath}'.");
        }

        IReadOnlyList<AndroidPlatformPackage> platforms;
        try
        {
            platforms = Directory
                .EnumerateDirectories(platformsPath)
                .Where(path => Path.GetFileName(path).StartsWith("android-", StringComparison.OrdinalIgnoreCase))
                .Select(BuildPackage)
                .OrderByDescending(package => package.ApiLevel ?? -1)
                .ThenBy(package => package.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new AndroidPlatformDetectionResult(
                AndroidPlatformDetectionStatus.InspectionFailed,
                sdkRoot,
                Array.Empty<AndroidPlatformPackage>(),
                $"Installed Android platforms could not be inspected: {ex.Message}");
        }

        if (platforms.Count == 0)
        {
            return new AndroidPlatformDetectionResult(
                AndroidPlatformDetectionStatus.NoPlatformsInstalled,
                sdkRoot,
                platforms,
                $"No installed android-* platform packages were found under '{platformsPath}'.");
        }

        var usableCount = platforms.Count(platform => platform.IsUsable);
        if (usableCount == 0)
        {
            return new AndroidPlatformDetectionResult(
                AndroidPlatformDetectionStatus.PartialInstallationsOnly,
                sdkRoot,
                platforms,
                "Android platform directories were found, but none contain both a resolvable API level and android.jar.");
        }

        var partialCount = platforms.Count - usableCount;
        var suffix = partialCount > 0
            ? $" {partialCount} additional partial/broken platform installation(s) are preserved as evidence."
            : string.Empty;
        return new AndroidPlatformDetectionResult(
            AndroidPlatformDetectionStatus.Succeeded,
            sdkRoot,
            platforms,
            $"Detected {usableCount} usable Android platform package(s).{suffix}");
    }

    private static AndroidPlatformPackage BuildPackage(string installationPath)
    {
        var packageId = Path.GetFileName(installationPath);
        var sourcePropertiesPath = Path.Combine(installationPath, "source.properties");
        var raw = ReadSourceProperties(sourcePropertiesPath, out var readError);
        var revision = ParseProperty(raw, "Pkg.Revision");
        var codeName = ParseProperty(raw, "AndroidVersion.CodeName");
        var metadataApi = ParseIntProperty(raw, "AndroidVersion.ApiLevel");
        var directoryApi = ParseDirectoryApiLevel(packageId);
        var apiLevel = metadataApi ?? directoryApi;
        var androidJarExists = File.Exists(Path.Combine(installationPath, "android.jar"));
        var frameworkAidlExists = File.Exists(Path.Combine(installationPath, "framework.aidl"));

        var messages = new List<string>();
        if (!string.IsNullOrWhiteSpace(readError))
            messages.Add(readError);
        else if (raw is null)
            messages.Add("source.properties is missing.");

        if (metadataApi is null && directoryApi is not null)
            messages.Add($"API level was inferred from directory name '{packageId}'.");
        else if (metadataApi is not null && directoryApi is not null && metadataApi != directoryApi)
            messages.Add($"Directory API {directoryApi} differs from source.properties API {metadataApi}; metadata value is retained.");
        else if (apiLevel is null)
            messages.Add("API level could not be resolved from source.properties or directory name.");

        if (!androidJarExists)
            messages.Add("android.jar is missing.");

        if (string.IsNullOrWhiteSpace(revision))
            messages.Add("Pkg.Revision is missing from source.properties.");

        return new AndroidPlatformPackage(
            packageId,
            Path.GetFullPath(installationPath),
            apiLevel,
            codeName,
            revision,
            androidJarExists,
            frameworkAidlExists,
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

    private static int? ParseIntProperty(string? raw, string propertyName)
        => int.TryParse(ParseProperty(raw, propertyName), out var value) && value >= 0
            ? value
            : null;

    private static int? ParseDirectoryApiLevel(string packageId)
    {
        const string prefix = "android-";
        if (!packageId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var suffix = packageId[prefix.Length..];
        return int.TryParse(suffix, out var value) && value >= 0 ? value : null;
    }
}
