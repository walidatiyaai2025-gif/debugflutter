using System.Diagnostics;
using System.Text.Json;
using FlutterBuildDoctor.Application.Environment;

namespace FlutterBuildDoctor.Infrastructure.Environment;

public sealed class AndroidStudioDetector : IAndroidStudioDetector
{
    private readonly IAndroidStudioInstallationSource _source;

    public AndroidStudioDetector(IAndroidStudioInstallationSource source)
        => _source = source ?? throw new ArgumentNullException(nameof(source));

    public AndroidStudioDetectionResult Detect(WindowsEnvironmentInfo windowsEnvironment)
    {
        ArgumentNullException.ThrowIfNull(windowsEnvironment);
        if (windowsEnvironment.Status == WindowsEnvironmentDetectionStatus.NotWindows)
            return new AndroidStudioDetectionResult(AndroidStudioDetectionStatus.NotWindows, Array.Empty<AndroidStudioInstallation>(), "Android Studio Windows installation discovery is unavailable because the current operating system is not Windows.");
        if (!windowsEnvironment.IsSuccess)
            return new AndroidStudioDetectionResult(AndroidStudioDetectionStatus.InspectionFailed, Array.Empty<AndroidStudioInstallation>(), "Android Studio discovery requires successful Windows environment detection.");

        IReadOnlyList<AndroidStudioExecutableEvidence> discovered;
        try { discovered = _source.Discover(); }
        catch (Exception ex)
        {
            return new AndroidStudioDetectionResult(AndroidStudioDetectionStatus.InspectionFailed, Array.Empty<AndroidStudioInstallation>(), $"Android Studio installation locations could not be inspected: {ex.Message}");
        }

        if (discovered.Count == 0)
            return new AndroidStudioDetectionResult(AndroidStudioDetectionStatus.Missing, Array.Empty<AndroidStudioInstallation>(), "No Android Studio installation was found in the supported Windows installation roots.");

        var installations = discovered.Select(BuildInstallation)
            .OrderBy(i => i.DiscoverySource)
            .ThenByDescending(i => ParseVersion(i.Version))
            .ThenBy(i => i.InstallationPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var incomplete = installations.Count(i => string.IsNullOrWhiteSpace(i.Version));
        return new AndroidStudioDetectionResult(AndroidStudioDetectionStatus.Succeeded, installations,
            $"Detected {installations.Length} Android Studio installation(s).{(incomplete > 0 ? $" {incomplete} installation(s) have incomplete version metadata but remain visible as evidence." : string.Empty)}");
    }

    private static AndroidStudioInstallation BuildInstallation(AndroidStudioExecutableEvidence evidence)
    {
        var executablePath = Path.GetFullPath(evidence.ExecutablePath);
        var bin = Path.GetDirectoryName(executablePath);
        var installationPath = !string.IsNullOrWhiteSpace(bin) && string.Equals(Path.GetFileName(bin), "bin", StringComparison.OrdinalIgnoreCase)
            ? Directory.GetParent(bin)?.FullName ?? bin : bin ?? executablePath;

        var productInfoPath = FindProductInfo(installationPath, bin);
        if (productInfoPath is not null)
        {
            var productInfo = TryReadProductInfo(productInfoPath, out var raw, out var error);
            if (productInfo is not null)
                return new AndroidStudioInstallation(executablePath, installationPath, productInfo.ProductName, productInfo.Version, productInfo.BuildNumber, productInfo.ProductCode, evidence.DiscoverySource, AndroidStudioMetadataSource.ProductInfoJson, raw, error);
            var fallback = BuildFromFallback(executablePath, installationPath, evidence.DiscoverySource);
            return fallback with { RawMetadata = raw, Message = JoinMessages(error, fallback.Message) };
        }
        return BuildFromFallback(executablePath, installationPath, evidence.DiscoverySource);
    }

    private static AndroidStudioInstallation BuildFromFallback(string executablePath, string installationPath, AndroidStudioDiscoverySource discoverySource)
    {
        var buildPath = Path.Combine(installationPath, "build.txt");
        if (File.Exists(buildPath))
        {
            try
            {
                var raw = File.ReadAllText(buildPath);
                var buildNumber = raw.Trim();
                var file = TryReadFileVersion(executablePath);
                return new AndroidStudioInstallation(executablePath, installationPath, file.ProductName ?? "Android Studio", file.Version,
                    buildNumber.Length == 0 ? null : buildNumber, ProductCodeFromBuild(buildNumber), discoverySource,
                    AndroidStudioMetadataSource.BuildTxt, raw, buildNumber.Length == 0 ? "build.txt is empty." : file.Message);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                var file = TryReadFileVersion(executablePath);
                return new AndroidStudioInstallation(executablePath, installationPath, file.ProductName ?? "Android Studio", file.Version, null, null, discoverySource,
                    file.Version is null ? AndroidStudioMetadataSource.None : AndroidStudioMetadataSource.ExecutableFileVersion, null,
                    JoinMessages($"build.txt could not be read: {ex.Message}", file.Message));
            }
        }

        var fileVersion = TryReadFileVersion(executablePath);
        return new AndroidStudioInstallation(executablePath, installationPath, fileVersion.ProductName ?? "Android Studio", fileVersion.Version, null, null, discoverySource,
            fileVersion.Version is null ? AndroidStudioMetadataSource.None : AndroidStudioMetadataSource.ExecutableFileVersion, null,
            fileVersion.Message ?? (fileVersion.Version is null ? "Android Studio version metadata was not available." : null));
    }

    private static string? FindProductInfo(string installationPath, string? binPath)
    {
        foreach (var candidate in new[] { Path.Combine(installationPath, "product-info.json"), string.IsNullOrWhiteSpace(binPath) ? null : Path.Combine(binPath, "product-info.json") })
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate)) return candidate;
        return null;
    }

    private static ProductInfo? TryReadProductInfo(string path, out string? raw, out string? error)
    {
        raw = null; error = null;
        try
        {
            raw = File.ReadAllText(path);
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            var name = ReadString(root, "name");
            var version = ReadString(root, "version");
            var buildNumber = ReadString(root, "buildNumber");
            var productCode = ReadString(root, "productCode");
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(version) && string.IsNullOrWhiteSpace(buildNumber))
            { error = "product-info.json did not contain recognizable Android Studio identity fields."; return null; }
            return new ProductInfo(name ?? "Android Studio", version, buildNumber, productCode);
        }
        catch (JsonException ex) { error = $"product-info.json could not be parsed: {ex.Message}"; return null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { error = $"product-info.json could not be read: {ex.Message}"; return null; }
    }

    private static string? ReadString(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;

    private static FileVersionEvidence TryReadFileVersion(string executablePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(executablePath);
            return new FileVersionEvidence(FirstNonBlank(info.ProductName, info.FileDescription), FirstNonBlank(info.ProductVersion, info.FileVersion), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { return new FileVersionEvidence(null, null, $"Executable file version could not be read: {ex.Message}"); }
    }

    private static string? FirstNonBlank(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
    private static string? ProductCodeFromBuild(string? buildNumber)
    {
        if (string.IsNullOrWhiteSpace(buildNumber)) return null;
        var separator = buildNumber.IndexOf('-');
        return separator > 0 ? buildNumber[..separator].Trim() : null;
    }
    private static string? JoinMessages(string? first, string? second)
    {
        var values = new[] { first, second }.Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
        return values.Length == 0 ? null : string.Join(" ", values);
    }
    private static Version ParseVersion(string? value)
    {
        if (Version.TryParse(value, out var parsed)) return parsed;
        if (!string.IsNullOrWhiteSpace(value))
        {
            var numeric = new string(value.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()).TrimEnd('.');
            if (Version.TryParse(numeric, out parsed)) return parsed;
        }
        return new Version(0, 0);
    }

    private sealed record ProductInfo(string ProductName, string? Version, string? BuildNumber, string? ProductCode);
    private sealed record FileVersionEvidence(string? ProductName, string? Version, string? Message);
}
