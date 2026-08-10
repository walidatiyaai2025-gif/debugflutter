using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FlutterBuildDoctor.Flutter.Release;

public sealed partial class ReleasePackageInspector : IReleasePackageInspector
{
    public ReleaseCheck Inspect(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        var applicationId = TryReadGradleApplicationId(projectRoot) ?? TryReadManifestPackage(projectRoot);
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            return new ReleaseCheck(
                "release.package-id",
                ReleaseCheckStatus.Blocker,
                "Application/package ID could not be resolved.",
                new[] { "Checked android/app/build.gradle(.kts) and AndroidManifest.xml." });
        }

        if (!PackageIdRegex().IsMatch(applicationId))
        {
            return new ReleaseCheck(
                "release.package-id",
                ReleaseCheckStatus.Blocker,
                "Application/package ID is not valid for release.",
                new[] { $"Resolved ID: {applicationId}" });
        }

        return new ReleaseCheck(
            "release.package-id",
            ReleaseCheckStatus.Ready,
            "Application/package ID is present and structurally valid.",
            new[] { $"Application ID: {applicationId}" });
    }

    private static string? TryReadGradleApplicationId(string projectRoot)
    {
        foreach (var file in new[]
                 {
                     Path.Combine(projectRoot, "android", "app", "build.gradle.kts"),
                     Path.Combine(projectRoot, "android", "app", "build.gradle")
                 })
        {
            if (!File.Exists(file)) continue;
            var text = File.ReadAllText(file);
            var match = Regex.Match(
                text,
                "applicationId\\s*(?:=\\s*)?[\\\"'](?<id>[^\\\"']+)[\\\"']",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success) return match.Groups["id"].Value.Trim();
        }

        return null;
    }

    private static string? TryReadManifestPackage(string projectRoot)
    {
        var manifest = Path.Combine(projectRoot, "android", "app", "src", "main", "AndroidManifest.xml");
        if (!File.Exists(manifest)) return null;
        try
        {
            return XDocument.Load(manifest).Root?.Attribute("package")?.Value?.Trim();
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException)
        {
            return null;
        }
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]*(?:\\.[A-Za-z][A-Za-z0-9_]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageIdRegex();
}

public sealed partial class ReleaseVersionInspector : IReleaseVersionInspector
{
    public ReleaseCheck Inspect(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        var pubspec = Path.Combine(projectRoot, "pubspec.yaml");
        if (!File.Exists(pubspec))
        {
            return Blocker("pubspec.yaml was not found.");
        }

        var match = VersionLineRegex().Match(File.ReadAllText(pubspec));
        if (!match.Success)
        {
            return Blocker("pubspec.yaml does not declare a versionName+versionCode value.");
        }

        var value = match.Groups["value"].Value.Trim();
        var separator = value.LastIndexOf('+');
        if (separator <= 0 || separator == value.Length - 1)
        {
            return Blocker($"Version '{value}' must include both version name and positive build code (for example 1.2.3+45).");
        }

        var versionName = value[..separator];
        var codeText = value[(separator + 1)..];
        if (!VersionNameRegex().IsMatch(versionName) || !int.TryParse(codeText, out var versionCode) || versionCode <= 0)
        {
            return Blocker($"Version '{value}' is not release-ready.");
        }

        return new ReleaseCheck(
            "release.version",
            ReleaseCheckStatus.Ready,
            "Version name and version code are release-ready.",
            new[] { $"versionName={versionName}", $"versionCode={versionCode}" });
    }

    private static ReleaseCheck Blocker(string message)
        => new("release.version", ReleaseCheckStatus.Blocker, message, Array.Empty<string>());

    [GeneratedRegex("(?m)^\\s*version:\\s*(?<value>[^\\s#]+)", RegexOptions.CultureInvariant)]
    private static partial Regex VersionLineRegex();

    [GeneratedRegex("^\\d+\\.\\d+\\.\\d+(?:-[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionNameRegex();
}

public sealed class ReleaseSigningInspector : IReleaseSigningInspector
{
    public ReleaseCheck Inspect(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        var keyProperties = Path.Combine(projectRoot, "android", "key.properties");
        if (!File.Exists(keyProperties))
        {
            return new ReleaseCheck(
                "release.signing",
                ReleaseCheckStatus.Blocker,
                "Release signing configuration is missing.",
                new[] { "android/key.properties not found." });
        }

        var values = ReadKeysWithoutLoggingValues(keyProperties);
        var required = new[] { "storeFile", "storePassword", "keyAlias", "keyPassword" };
        var missing = required.Where(key => !values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)).ToArray();
        var storeExists = false;
        if (missing.Length == 0)
        {
            var storeFile = values["storeFile"];
            try
            {
                var path = Path.IsPathRooted(storeFile)
                    ? Path.GetFullPath(storeFile)
                    : Path.GetFullPath(Path.Combine(projectRoot, "android", "app", storeFile));
                storeExists = File.Exists(path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                storeExists = false;
            }
        }

        var evidence = new List<string>
        {
            "android/key.properties present.",
            $"Required signing keys present: {required.Length - missing.Length}/{required.Length}.",
            $"Referenced keystore file present: {(storeExists ? "yes" : "no")}.",
            "Signing secret values are intentionally not included in this report."
        };
        if (missing.Length > 0)
            evidence.Add($"Missing settings: {string.Join(", ", missing)}.");

        return missing.Length == 0 && storeExists
            ? new ReleaseCheck("release.signing", ReleaseCheckStatus.Ready, "Release signing configuration is present.", evidence)
            : new ReleaseCheck("release.signing", ReleaseCheckStatus.Blocker, "Release signing configuration is incomplete.", evidence);
    }

    private static Dictionary<string, string> ReadKeysWithoutLoggingValues(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            var separator = trimmed.IndexOf('=');
            if (separator <= 0) continue;
            result[trimmed[..separator].Trim()] = trimmed[(separator + 1)..].Trim();
        }

        return result;
    }
}

public sealed class ReleaseManifestInspector : IReleaseManifestInspector
{
    private static readonly XNamespace Android = "http://schemas.android.com/apk/res/android";

    public ReleaseCheck Inspect(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        var manifest = Path.Combine(projectRoot, "android", "app", "src", "main", "AndroidManifest.xml");
        if (!File.Exists(manifest))
            return new ReleaseCheck("release.manifest", ReleaseCheckStatus.Blocker, "AndroidManifest.xml was not found.", Array.Empty<string>());

        try
        {
            var document = XDocument.Load(manifest);
            var application = document.Root?.Elements().FirstOrDefault(element => element.Name.LocalName == "application");
            if (application is null)
                return new ReleaseCheck("release.manifest", ReleaseCheckStatus.Blocker, "Manifest has no application element.", Array.Empty<string>());

            if (string.Equals(application.Attribute(Android + "debuggable")?.Value, "true", StringComparison.OrdinalIgnoreCase))
            {
                return new ReleaseCheck(
                    "release.manifest",
                    ReleaseCheckStatus.Blocker,
                    "Main manifest explicitly enables android:debuggable=true.",
                    new[] { "Release manifest must not explicitly force debuggable mode." });
            }

            var hasLauncher = application
                .Descendants()
                .Where(element => element.Name.LocalName == "intent-filter")
                .Any(filter =>
                    filter.Elements().Any(element => element.Name.LocalName == "action" &&
                        string.Equals(element.Attribute(Android + "name")?.Value, "android.intent.action.MAIN", StringComparison.Ordinal)) &&
                    filter.Elements().Any(element => element.Name.LocalName == "category" &&
                        string.Equals(element.Attribute(Android + "name")?.Value, "android.intent.category.LAUNCHER", StringComparison.Ordinal)));
            if (!hasLauncher)
                return new ReleaseCheck("release.manifest", ReleaseCheckStatus.Blocker, "No MAIN/LAUNCHER activity intent filter was found.", Array.Empty<string>());

            var hasLabel = !string.IsNullOrWhiteSpace(application.Attribute(Android + "label")?.Value);
            return new ReleaseCheck(
                "release.manifest",
                hasLabel ? ReleaseCheckStatus.Ready : ReleaseCheckStatus.Warning,
                hasLabel ? "Android manifest release checks passed." : "Launcher manifest is valid but application label is missing.",
                new[] { "MAIN/LAUNCHER intent filter present.", "android:debuggable is not forced true." });
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException)
        {
            return new ReleaseCheck(
                "release.manifest",
                ReleaseCheckStatus.Blocker,
                "Android manifest could not be parsed.",
                new[] { ex.Message });
        }
    }
}
