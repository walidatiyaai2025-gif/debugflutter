using System.IO;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace FlutterBuildDoctor.Flutter.ProjectAnalysis;

public sealed class PubspecLockParser : IPubspecLockParser
{
    private const long MaxLockFileBytes = 8 * 1024 * 1024;

    public PubspecLockParseResult Parse(FlutterProjectRootResult projectRoot)
    {
        ArgumentNullException.ThrowIfNull(projectRoot);

        if (!projectRoot.IsSuccess || string.IsNullOrWhiteSpace(projectRoot.EffectiveRoot))
        {
            return Result(
                PubspecLockParseStatus.ProjectRootUnavailable,
                projectRoot,
                null,
                null,
                "A successfully resolved Flutter project root is required before pubspec.lock parsing.");
        }

        string rootPath;
        string lockPath;
        try
        {
            rootPath = Path.GetFullPath(projectRoot.EffectiveRoot);
            lockPath = Path.GetFullPath(Path.Combine(rootPath, "pubspec.lock"));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Result(
                PubspecLockParseStatus.InvalidRequest,
                projectRoot,
                null,
                null,
                $"Resolved project path is invalid: {ex.Message}");
        }

        if (!File.Exists(lockPath))
        {
            return Result(
                PubspecLockParseStatus.LockFileNotFound,
                projectRoot,
                lockPath,
                null,
                "pubspec.lock is not present. No package resolution command was run.");
        }

        string rawText;
        try
        {
            var attributes = File.GetAttributes(lockPath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                return Result(
                    PubspecLockParseStatus.InvalidRequest,
                    projectRoot,
                    lockPath,
                    null,
                    "pubspec.lock is a reparse point or symbolic link and will not be followed outside the imported project evidence boundary.");
            }

            var fileInfo = new FileInfo(lockPath);
            if (fileInfo.Length > MaxLockFileBytes)
            {
                return Result(
                    PubspecLockParseStatus.FileTooLarge,
                    projectRoot,
                    lockPath,
                    null,
                    $"pubspec.lock is larger than the {MaxLockFileBytes} byte parsing safety limit.");
            }

            rawText = File.ReadAllText(lockPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Result(
                PubspecLockParseStatus.ReadFailed,
                projectRoot,
                lockPath,
                null,
                $"pubspec.lock could not be read: {ex.Message}");
        }

        YamlStream yaml;
        try
        {
            yaml = new YamlStream();
            yaml.Load(new StringReader(rawText));
        }
        catch (YamlException ex)
        {
            return Result(
                PubspecLockParseStatus.MalformedYaml,
                projectRoot,
                lockPath,
                rawText,
                $"pubspec.lock is malformed YAML: {ex.Message}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return Result(
                PubspecLockParseStatus.MalformedYaml,
                projectRoot,
                lockPath,
                rawText,
                $"pubspec.lock could not be parsed: {ex.Message}");
        }

        if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode root)
        {
            return Result(
                PubspecLockParseStatus.InvalidDocument,
                projectRoot,
                lockPath,
                rawText,
                "pubspec.lock must contain exactly one YAML mapping document.");
        }

        if (Node(root, "packages") is not YamlMappingNode packagesNode)
        {
            return Result(
                PubspecLockParseStatus.InvalidDocument,
                projectRoot,
                lockPath,
                rawText,
                "pubspec.lock must contain a 'packages' mapping.");
        }

        if (ValidateSdkShape(root) is { } sdkShapeError)
        {
            return Result(
                PubspecLockParseStatus.InvalidDocument,
                projectRoot,
                lockPath,
                rawText,
                sdkShapeError);
        }

        var packages = new List<PubspecLockedPackage>(packagesNode.Children.Count);
        foreach (var pair in packagesNode.Children)
        {
            if (pair.Key is not YamlScalarNode nameNode || string.IsNullOrWhiteSpace(nameNode.Value) ||
                pair.Value is not YamlMappingNode packageNode)
            {
                return Result(
                    PubspecLockParseStatus.InvalidDocument,
                    projectRoot,
                    lockPath,
                    rawText,
                    "pubspec.lock contains an invalid package entry.");
            }

            var packageName = nameNode.Value.Trim();
            if (ValidatePackageShape(packageName, packageNode) is { } packageShapeError)
            {
                return Result(
                    PubspecLockParseStatus.InvalidDocument,
                    projectRoot,
                    lockPath,
                    rawText,
                    packageShapeError);
            }

            var version = Scalar(packageNode, "version")?.Trim();
            var sourceText = Scalar(packageNode, "source")?.Trim();
            var dependencyType = Scalar(packageNode, "dependency")?.Trim();
            if (string.IsNullOrWhiteSpace(version) ||
                string.IsNullOrWhiteSpace(sourceText) ||
                string.IsNullOrWhiteSpace(dependencyType))
            {
                return Result(
                    PubspecLockParseStatus.InvalidDocument,
                    projectRoot,
                    lockPath,
                    rawText,
                    $"pubspec.lock package '{packageName}' is missing its locked version, source, or dependency relationship.");
            }

            var source = ParseSource(sourceText);
            var description = Node(packageNode, "description");
            string? descriptionName = null;
            string? descriptionUrl = null;
            string? descriptionPath = null;
            string? sha256 = null;
            string? gitRef = null;
            string? gitResolvedRef = null;
            string? gitUrl = null;

            if (description is YamlScalarNode scalarDescription)
            {
                if (source == PubspecLockedPackageSource.Path)
                    descriptionPath = scalarDescription.Value;
                else if (source == PubspecLockedPackageSource.Sdk)
                    descriptionName = scalarDescription.Value;
            }
            else if (description is YamlMappingNode descriptionMap)
            {
                descriptionName = Scalar(descriptionMap, "name");
                descriptionUrl = SanitizeUrlEvidence(Scalar(descriptionMap, "url"));
                descriptionPath = Scalar(descriptionMap, "path");
                sha256 = Scalar(descriptionMap, "sha256");
                gitRef = source == PubspecLockedPackageSource.Git
                    ? Scalar(descriptionMap, "ref")
                    : null;
                gitResolvedRef = source == PubspecLockedPackageSource.Git
                    ? Scalar(descriptionMap, "resolved-ref")
                    : null;
                gitUrl = source == PubspecLockedPackageSource.Git
                    ? SanitizeUrlEvidence(Scalar(descriptionMap, "url"))
                    : null;
            }

            packages.Add(new PubspecLockedPackage(
                packageName,
                version,
                source,
                dependencyType,
                descriptionName,
                descriptionUrl,
                descriptionPath,
                sha256,
                gitRef,
                gitResolvedRef,
                gitUrl));
        }

        var sdks = Node(root, "sdks") as YamlMappingNode;
        var metadata = new PubspecLockMetadata(
            packages.OrderBy(package => package.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            sdks is null ? null : Scalar(sdks, "dart"),
            sdks is null ? null : Scalar(sdks, "flutter"));

        return new PubspecLockParseResult(
            PubspecLockParseStatus.Succeeded,
            projectRoot,
            lockPath,
            metadata,
            rawText,
            $"Parsed {metadata.Packages.Count} locked package(s) without modifying dependency resolution.");
    }

    private static string? ValidateSdkShape(YamlMappingNode root)
    {
        if (Node(root, "sdks") is not { } sdksNode)
            return null;

        if (sdksNode is not YamlMappingNode sdks)
            return "pubspec.lock section 'sdks' must be a mapping.";

        foreach (var key in new[] { "dart", "flutter" })
        {
            if (Node(sdks, key) is { } value && value is not YamlScalarNode)
                return $"pubspec.lock field 'sdks.{key}' must be a scalar value.";
        }

        return null;
    }

    private static string? ValidatePackageShape(string packageName, YamlMappingNode packageNode)
    {
        foreach (var key in new[] { "dependency", "source", "version" })
        {
            if (Node(packageNode, key) is { } value && value is not YamlScalarNode)
                return $"pubspec.lock package '{packageName}' field '{key}' must be a scalar value.";
        }

        if (Node(packageNode, "description") is { } description)
        {
            if (description is not YamlScalarNode && description is not YamlMappingNode)
                return $"pubspec.lock package '{packageName}' has an invalid description shape.";

            if (description is YamlMappingNode descriptionMap)
            {
                foreach (var key in new[] { "name", "url", "path", "sha256", "ref", "resolved-ref" })
                {
                    if (Node(descriptionMap, key) is { } value && value is not YamlScalarNode)
                        return $"pubspec.lock package '{packageName}' description field '{key}' must be a scalar value.";
                }
            }
        }

        return null;
    }

    private static PubspecLockedPackageSource ParseSource(string value)
        => value.ToLowerInvariant() switch
        {
            "hosted" => PubspecLockedPackageSource.Hosted,
            "git" => PubspecLockedPackageSource.Git,
            "path" => PubspecLockedPackageSource.Path,
            "sdk" => PubspecLockedPackageSource.Sdk,
            _ => PubspecLockedPackageSource.Unknown
        };

    private static string? Scalar(YamlMappingNode mapping, string key)
        => (Node(mapping, key) as YamlScalarNode)?.Value;

    private static YamlNode? Node(YamlMappingNode mapping, string key)
    {
        foreach (var pair in mapping.Children)
        {
            if (pair.Key is YamlScalarNode scalar &&
                string.Equals(scalar.Value, key, StringComparison.Ordinal))
                return pair.Value;
        }

        return null;
    }

    private static string? SanitizeUrlEvidence(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var trimmed = value.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
        {
            if (string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment))
                return trimmed;

            try
            {
                var builder = new UriBuilder(uri)
                {
                    UserName = string.Empty,
                    Password = string.Empty,
                    Query = string.Empty,
                    Fragment = string.Empty
                };
                return builder.Uri.AbsoluteUri;
            }
            catch (UriFormatException)
            {
                return "[redacted-url]";
            }
        }

        var atIndex = trimmed.IndexOf('@');
        if (atIndex > 0 && trimmed[..atIndex].Contains(':'))
            return "[redacted]" + trimmed[atIndex..];

        return trimmed;
    }

    private static PubspecLockParseResult Result(
        PubspecLockParseStatus status,
        FlutterProjectRootResult projectRoot,
        string? lockFilePath,
        string? rawText,
        string message)
        => new(status, projectRoot, lockFilePath, null, rawText, message);
}
