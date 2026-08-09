using System.IO;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace FlutterBuildDoctor.Flutter.ProjectAnalysis;

public sealed class PubspecParser : IPubspecParser
{
    private const long MaxPubspecBytes = 2 * 1024 * 1024;

    private static readonly string[] ScalarRootKeys =
    {
        "name",
        "description",
        "version",
        "publish_to",
        "homepage",
        "repository",
        "issue_tracker",
        "documentation"
    };

    private static readonly (string Key, PubspecDependencySection Section)[] DependencySections =
    {
        ("dependencies", PubspecDependencySection.Dependencies),
        ("dev_dependencies", PubspecDependencySection.DevDependencies),
        ("dependency_overrides", PubspecDependencySection.DependencyOverrides)
    };

    public PubspecParseResult Parse(FlutterProjectRootResult projectRoot)
    {
        ArgumentNullException.ThrowIfNull(projectRoot);

        if (!projectRoot.IsSuccess ||
            string.IsNullOrWhiteSpace(projectRoot.EffectiveRoot) ||
            string.IsNullOrWhiteSpace(projectRoot.EffectivePubspecPath))
        {
            return Result(
                PubspecParseStatus.ProjectRootUnavailable,
                projectRoot,
                projectRoot.EffectivePubspecPath,
                null,
                "A successfully resolved Flutter project root is required before pubspec parsing.");
        }

        string rootPath;
        string pubspecPath;
        try
        {
            rootPath = Path.GetFullPath(projectRoot.EffectiveRoot);
            pubspecPath = Path.GetFullPath(projectRoot.EffectivePubspecPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Result(
                PubspecParseStatus.InvalidRequest,
                projectRoot,
                projectRoot.EffectivePubspecPath,
                null,
                $"Resolved project paths are invalid: {ex.Message}");
        }

        var expectedPubspecPath = Path.GetFullPath(Path.Combine(rootPath, "pubspec.yaml"));
        if (!PathsEqual(pubspecPath, expectedPubspecPath))
        {
            return Result(
                PubspecParseStatus.InvalidRequest,
                projectRoot,
                pubspecPath,
                null,
                "The resolved pubspec path must be the pubspec.yaml directly inside the effective project root.");
        }

        if (!File.Exists(pubspecPath))
        {
            return Result(
                PubspecParseStatus.PubspecNotFound,
                projectRoot,
                pubspecPath,
                null,
                "The resolved pubspec.yaml no longer exists.");
        }

        string rawText;
        try
        {
            var attributes = File.GetAttributes(pubspecPath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                return Result(
                    PubspecParseStatus.InvalidRequest,
                    projectRoot,
                    pubspecPath,
                    null,
                    "The resolved pubspec.yaml is a reparse point or symbolic link and will not be followed outside the imported project evidence boundary.");
            }

            var fileInfo = new FileInfo(pubspecPath);
            if (fileInfo.Length > MaxPubspecBytes)
            {
                return Result(
                    PubspecParseStatus.FileTooLarge,
                    projectRoot,
                    pubspecPath,
                    null,
                    $"pubspec.yaml is larger than the {MaxPubspecBytes} byte parsing safety limit.");
            }

            rawText = File.ReadAllText(pubspecPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Result(
                PubspecParseStatus.ReadFailed,
                projectRoot,
                pubspecPath,
                null,
                $"pubspec.yaml could not be read: {ex.Message}");
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
                PubspecParseStatus.MalformedYaml,
                projectRoot,
                pubspecPath,
                rawText,
                $"pubspec.yaml is malformed YAML: {ex.Message}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return Result(
                PubspecParseStatus.MalformedYaml,
                projectRoot,
                pubspecPath,
                rawText,
                $"pubspec.yaml could not be parsed: {ex.Message}");
        }

        if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode root)
        {
            return Result(
                PubspecParseStatus.InvalidDocument,
                projectRoot,
                pubspecPath,
                rawText,
                "pubspec.yaml must contain exactly one YAML mapping document.");
        }

        if (ValidateKnownDocumentShapes(root) is { } shapeError)
        {
            return Result(
                PubspecParseStatus.InvalidDocument,
                projectRoot,
                pubspecPath,
                rawText,
                shapeError);
        }

        var name = Scalar(root, "name")?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result(
                PubspecParseStatus.MissingRequiredField,
                projectRoot,
                pubspecPath,
                rawText,
                "pubspec.yaml is missing the required project/package name.");
        }

        var environment = Mapping(root, "environment");
        var metadata = new PubspecMetadata(
            name,
            Scalar(root, "description"),
            Scalar(root, "version"),
            Scalar(root, "publish_to"),
            SanitizeUrlEvidence(Scalar(root, "homepage")),
            SanitizeUrlEvidence(Scalar(root, "repository")),
            SanitizeUrlEvidence(Scalar(root, "issue_tracker")),
            SanitizeUrlEvidence(Scalar(root, "documentation")),
            environment is null ? null : Scalar(environment, "sdk"),
            environment is null ? null : Scalar(environment, "flutter"),
            ScalarSequence(root, "topics"),
            ParseDependencies(root));

        return new PubspecParseResult(
            PubspecParseStatus.Succeeded,
            projectRoot,
            pubspecPath,
            metadata,
            rawText,
            $"Parsed pubspec.yaml for '{metadata.Name}' without modifying the project.");
    }

    private static string? ValidateKnownDocumentShapes(YamlMappingNode root)
    {
        foreach (var key in ScalarRootKeys)
        {
            if (Node(root, key) is { } node && node is not YamlScalarNode)
                return $"pubspec.yaml field '{key}' must be a scalar value.";
        }

        if (Node(root, "environment") is { } environmentNode)
        {
            if (environmentNode is not YamlMappingNode environment)
                return "pubspec.yaml section 'environment' must be a mapping.";

            foreach (var key in new[] { "sdk", "flutter" })
            {
                if (Node(environment, key) is { } value && value is not YamlScalarNode)
                    return $"pubspec.yaml field 'environment.{key}' must be a scalar value.";
            }
        }

        if (Node(root, "topics") is { } topicsNode)
        {
            if (topicsNode is not YamlSequenceNode topics)
                return "pubspec.yaml section 'topics' must be a sequence.";

            if (topics.Children.Any(child => child is not YamlScalarNode))
                return "pubspec.yaml section 'topics' may contain scalar values only.";
        }

        foreach (var (key, _) in DependencySections)
        {
            if (Node(root, key) is not { } sectionNode)
                continue;

            if (sectionNode is not YamlMappingNode section)
                return $"pubspec.yaml section '{key}' must be a mapping.";

            foreach (var pair in section.Children)
            {
                if (pair.Key is not YamlScalarNode nameNode || string.IsNullOrWhiteSpace(nameNode.Value))
                    return $"pubspec.yaml section '{key}' contains a dependency with an invalid name.";

                if (pair.Value is not YamlScalarNode && pair.Value is not YamlMappingNode)
                    return $"pubspec.yaml dependency '{nameNode.Value}' in '{key}' must use a scalar or mapping specification.";

                if (pair.Value is YamlMappingNode dependency &&
                    ValidateDependencyShape(key, nameNode.Value, dependency) is { } dependencyError)
                    return dependencyError;
            }
        }

        return null;
    }

    private static string? ValidateDependencyShape(string section, string dependencyName, YamlMappingNode dependency)
    {
        foreach (var key in new[] { "version", "sdk", "path" })
        {
            if (Node(dependency, key) is { } node && node is not YamlScalarNode)
                return $"pubspec.yaml dependency '{dependencyName}' field '{key}' in '{section}' must be a scalar value.";
        }

        if (Node(dependency, "git") is { } gitNode)
        {
            if (gitNode is not YamlScalarNode && gitNode is not YamlMappingNode)
                return $"pubspec.yaml dependency '{dependencyName}' field 'git' in '{section}' must be a scalar or mapping.";

            if (gitNode is YamlMappingNode git)
            {
                foreach (var key in new[] { "url", "ref", "path" })
                {
                    if (Node(git, key) is { } value && value is not YamlScalarNode)
                        return $"pubspec.yaml dependency '{dependencyName}' field 'git.{key}' in '{section}' must be a scalar value.";
                }
            }
        }

        if (Node(dependency, "hosted") is { } hostedNode)
        {
            if (hostedNode is not YamlScalarNode && hostedNode is not YamlMappingNode)
                return $"pubspec.yaml dependency '{dependencyName}' field 'hosted' in '{section}' must be a scalar or mapping.";

            if (hostedNode is YamlMappingNode hosted)
            {
                foreach (var key in new[] { "url", "name" })
                {
                    if (Node(hosted, key) is { } value && value is not YamlScalarNode)
                        return $"pubspec.yaml dependency '{dependencyName}' field 'hosted.{key}' in '{section}' must be a scalar value.";
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<PubspecDependency> ParseDependencies(YamlMappingNode root)
    {
        var dependencies = new List<PubspecDependency>();
        foreach (var (key, section) in DependencySections)
            AddDependencies(root, key, section, dependencies);
        return dependencies.ToArray();
    }

    private static void AddDependencies(
        YamlMappingNode root,
        string key,
        PubspecDependencySection section,
        ICollection<PubspecDependency> destination)
    {
        if (Node(root, key) is not YamlMappingNode mapping)
            return;

        foreach (var pair in mapping.Children)
        {
            if (pair.Key is not YamlScalarNode nameNode || string.IsNullOrWhiteSpace(nameNode.Value))
                continue;

            destination.Add(ParseDependency(nameNode.Value.Trim(), section, pair.Value));
        }
    }

    private static PubspecDependency ParseDependency(
        string name,
        PubspecDependencySection section,
        YamlNode node)
    {
        if (node is YamlScalarNode scalar)
        {
            return new PubspecDependency(
                name,
                section,
                PubspecDependencyKind.Hosted,
                scalar.Value,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        if (node is not YamlMappingNode mapping)
            return UnknownDependency(name, section);

        var constraint = Scalar(mapping, "version");
        var sdk = Scalar(mapping, "sdk");
        if (!string.IsNullOrWhiteSpace(sdk))
        {
            return new PubspecDependency(
                name,
                section,
                PubspecDependencyKind.Sdk,
                constraint,
                sdk,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        var path = Scalar(mapping, "path");
        if (!string.IsNullOrWhiteSpace(path))
        {
            return new PubspecDependency(
                name,
                section,
                PubspecDependencyKind.Path,
                constraint,
                null,
                path,
                null,
                null,
                null,
                null,
                null);
        }

        if (Node(mapping, "git") is { } gitNode)
        {
            string? gitUrl = null;
            string? gitRef = null;
            string? gitPath = null;

            if (gitNode is YamlScalarNode gitScalar)
            {
                gitUrl = gitScalar.Value;
            }
            else if (gitNode is YamlMappingNode gitMapping)
            {
                gitUrl = Scalar(gitMapping, "url");
                gitRef = Scalar(gitMapping, "ref");
                gitPath = Scalar(gitMapping, "path");
            }

            return new PubspecDependency(
                name,
                section,
                PubspecDependencyKind.Git,
                constraint,
                null,
                null,
                SanitizeUrlEvidence(gitUrl),
                gitRef,
                gitPath,
                null,
                null);
        }

        if (Node(mapping, "hosted") is { } hostedNode)
        {
            string? hostedUrl = null;
            string? hostedName = null;

            if (hostedNode is YamlScalarNode hostedScalar)
            {
                hostedUrl = hostedScalar.Value;
            }
            else if (hostedNode is YamlMappingNode hostedMapping)
            {
                hostedUrl = Scalar(hostedMapping, "url");
                hostedName = Scalar(hostedMapping, "name");
            }

            return new PubspecDependency(
                name,
                section,
                PubspecDependencyKind.Hosted,
                constraint,
                null,
                null,
                null,
                null,
                null,
                SanitizeUrlEvidence(hostedUrl),
                hostedName);
        }

        return new PubspecDependency(
            name,
            section,
            PubspecDependencyKind.Unknown,
            constraint,
            null,
            null,
            null,
            null,
            null,
            null,
            null);
    }

    private static PubspecDependency UnknownDependency(string name, PubspecDependencySection section)
        => new(
            name,
            section,
            PubspecDependencyKind.Unknown,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);

    private static IReadOnlyList<string> ScalarSequence(YamlMappingNode mapping, string key)
    {
        if (Node(mapping, key) is not YamlSequenceNode sequence)
            return Array.Empty<string>();

        return sequence.Children
            .OfType<YamlScalarNode>()
            .Select(node => node.Value?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();
    }

    private static YamlMappingNode? Mapping(YamlMappingNode mapping, string key)
        => Node(mapping, key) as YamlMappingNode;

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
        if (atIndex > 0)
        {
            var prefix = trimmed[..atIndex];
            if (prefix.Contains(':'))
                return "[redacted]" + trimmed[atIndex..];
        }

        return trimmed;
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static PubspecParseResult Result(
        PubspecParseStatus status,
        FlutterProjectRootResult projectRoot,
        string? pubspecPath,
        string? rawText,
        string message)
        => new(status, projectRoot, pubspecPath, null, rawText, message);
}
