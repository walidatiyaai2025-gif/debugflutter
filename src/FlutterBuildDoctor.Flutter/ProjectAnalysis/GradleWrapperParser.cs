using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Flutter.ProjectAnalysis;

public sealed class GradleWrapperParser : IGradleWrapperParser
{
    private const long MaxPropertiesBytes = 256 * 1024;

    private static readonly Regex DistributionFilePattern = new(
        @"^gradle-(?<version>.+)-(?<type>bin|all)\.zip$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public GradleWrapperParseResult Parse(FlutterProjectRootResult projectRoot)
    {
        ArgumentNullException.ThrowIfNull(projectRoot);

        if (!projectRoot.IsSuccess || string.IsNullOrWhiteSpace(projectRoot.EffectiveRoot))
        {
            return Result(
                GradleWrapperParseStatus.ProjectRootUnavailable,
                projectRoot,
                null,
                null,
                null,
                GradleDistributionType.Unknown,
                null,
                "A successfully resolved Flutter project root is required before Gradle wrapper parsing.");
        }

        string rootPath;
        string androidPath;
        string gradlePath;
        string wrapperPath;
        string propertiesPath;
        try
        {
            rootPath = Path.GetFullPath(projectRoot.EffectiveRoot);
            androidPath = Path.Combine(rootPath, "android");
            gradlePath = Path.Combine(androidPath, "gradle");
            wrapperPath = Path.Combine(gradlePath, "wrapper");
            propertiesPath = Path.Combine(wrapperPath, "gradle-wrapper.properties");
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Result(
                GradleWrapperParseStatus.ProjectRootUnavailable,
                projectRoot,
                null,
                null,
                null,
                GradleDistributionType.Unknown,
                null,
                $"Resolved project path is invalid: {ex.Message}");
        }

        if (!Directory.Exists(wrapperPath))
        {
            return Result(
                GradleWrapperParseStatus.WrapperDirectoryMissing,
                projectRoot,
                propertiesPath,
                null,
                null,
                GradleDistributionType.Unknown,
                null,
                "The Android Gradle wrapper directory is not present.");
        }

        try
        {
            foreach (var directory in new[] { rootPath, androidPath, gradlePath, wrapperPath })
            {
                if (Directory.Exists(directory) && IsReparsePoint(directory))
                {
                    return Result(
                        GradleWrapperParseStatus.UnsafePath,
                        projectRoot,
                        propertiesPath,
                        null,
                        null,
                        GradleDistributionType.Unknown,
                        null,
                        $"Gradle wrapper parsing stopped because '{directory}' is a reparse point or symbolic link.");
                }
            }

            if (!File.Exists(propertiesPath))
            {
                return Result(
                    GradleWrapperParseStatus.PropertiesFileMissing,
                    projectRoot,
                    propertiesPath,
                    null,
                    null,
                    GradleDistributionType.Unknown,
                    null,
                    "gradle-wrapper.properties is not present. Gradle was not executed or repaired.");
            }

            if (IsReparsePoint(propertiesPath))
            {
                return Result(
                    GradleWrapperParseStatus.UnsafePath,
                    projectRoot,
                    propertiesPath,
                    null,
                    null,
                    GradleDistributionType.Unknown,
                    null,
                    "gradle-wrapper.properties is a reparse point or symbolic link and will not be followed.");
            }

            var fileInfo = new FileInfo(propertiesPath);
            if (fileInfo.Length > MaxPropertiesBytes)
            {
                return Result(
                    GradleWrapperParseStatus.FileTooLarge,
                    projectRoot,
                    propertiesPath,
                    null,
                    null,
                    GradleDistributionType.Unknown,
                    null,
                    $"gradle-wrapper.properties exceeds the {MaxPropertiesBytes} byte parsing safety limit.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Result(
                GradleWrapperParseStatus.ReadFailed,
                projectRoot,
                propertiesPath,
                null,
                null,
                GradleDistributionType.Unknown,
                null,
                $"Gradle wrapper path inspection failed: {ex.Message}");
        }

        string rawText;
        try
        {
            rawText = File.ReadAllText(propertiesPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Result(
                GradleWrapperParseStatus.ReadFailed,
                projectRoot,
                propertiesPath,
                null,
                null,
                GradleDistributionType.Unknown,
                null,
                $"gradle-wrapper.properties could not be read: {ex.Message}");
        }

        IReadOnlyDictionary<string, string> properties;
        try
        {
            properties = ParseJavaProperties(rawText);
        }
        catch (FormatException ex)
        {
            return Result(
                GradleWrapperParseStatus.InvalidProperties,
                projectRoot,
                propertiesPath,
                null,
                null,
                GradleDistributionType.Unknown,
                rawText,
                $"gradle-wrapper.properties is malformed: {ex.Message}");
        }

        if (!properties.TryGetValue("distributionUrl", out var distributionUrl) || string.IsNullOrWhiteSpace(distributionUrl))
        {
            return Result(
                GradleWrapperParseStatus.DistributionUrlMissing,
                projectRoot,
                propertiesPath,
                null,
                null,
                GradleDistributionType.Unknown,
                rawText,
                "gradle-wrapper.properties does not contain a non-empty distributionUrl.");
        }

        distributionUrl = distributionUrl.Trim();
        var sanitizedUrl = SanitizeUrlEvidence(distributionUrl);
        if (!TryExtractVersion(distributionUrl, out var version, out var distributionType))
        {
            return Result(
                GradleWrapperParseStatus.VersionNotDetected,
                projectRoot,
                propertiesPath,
                sanitizedUrl,
                null,
                GradleDistributionType.Unknown,
                rawText,
                "The Gradle wrapper distribution URL was read, but its Gradle version could not be identified from a standard gradle-<version>-bin/all.zip file name.");
        }

        return Result(
            GradleWrapperParseStatus.Succeeded,
            projectRoot,
            propertiesPath,
            sanitizedUrl,
            version,
            distributionType,
            rawText,
            $"Detected Gradle wrapper {version} ({distributionType}) from gradle-wrapper.properties without executing Gradle.");
    }

    private static IReadOnlyDictionary<string, string> ParseJavaProperties(string rawText)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        var logicalLine = new StringBuilder();
        var normalized = rawText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

        foreach (var physicalLine in normalized.Split('\n'))
        {
            if (logicalLine.Length == 0)
            {
                var firstContent = physicalLine.TrimStart();
                if (firstContent.Length == 0 || firstContent[0] is '#' or '!')
                    continue;

                logicalLine.Append(physicalLine);
            }
            else
            {
                logicalLine.Append(physicalLine.TrimStart());
            }

            if (EndsWithContinuation(logicalLine))
            {
                logicalLine.Length--;
                continue;
            }

            ParseLogicalLine(logicalLine.ToString(), properties);
            logicalLine.Clear();
        }

        if (logicalLine.Length > 0)
            ParseLogicalLine(logicalLine.ToString(), properties);

        return properties;
    }

    private static void ParseLogicalLine(string line, IDictionary<string, string> properties)
    {
        var index = 0;
        while (index < line.Length && char.IsWhiteSpace(line[index]))
            index++;

        if (index >= line.Length || line[index] is '#' or '!')
            return;

        var keyStart = index;
        var escaped = false;
        var separatorIndex = -1;
        var separatorIsWhitespace = false;

        for (; index < line.Length; index++)
        {
            var current = line[index];
            if (!escaped && current == '\\')
            {
                escaped = true;
                continue;
            }

            if (!escaped && (current is '=' or ':' || char.IsWhiteSpace(current)))
            {
                separatorIndex = index;
                separatorIsWhitespace = char.IsWhiteSpace(current);
                break;
            }

            escaped = false;
        }

        var keyEnd = separatorIndex >= 0 ? separatorIndex : line.Length;
        var valueStart = keyEnd;
        if (separatorIndex >= 0)
        {
            if (separatorIsWhitespace)
            {
                while (valueStart < line.Length && char.IsWhiteSpace(line[valueStart]))
                    valueStart++;

                if (valueStart < line.Length && line[valueStart] is '=' or ':')
                    valueStart++;
            }
            else
            {
                valueStart++;
            }

            while (valueStart < line.Length && char.IsWhiteSpace(line[valueStart]))
                valueStart++;
        }

        var key = UnescapeProperty(line[keyStart..keyEnd]);
        if (key.Length == 0)
            throw new FormatException("A property key is empty.");

        var value = valueStart < line.Length ? UnescapeProperty(line[valueStart..]) : string.Empty;
        properties[key] = value;
    }

    private static string UnescapeProperty(string value)
    {
        if (!value.Contains('\\'))
            return value;

        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (current != '\\')
            {
                builder.Append(current);
                continue;
            }

            if (++index >= value.Length)
                throw new FormatException("A property value ends with an incomplete escape sequence.");

            current = value[index];
            switch (current)
            {
                case 't': builder.Append('\t'); break;
                case 'n': builder.Append('\n'); break;
                case 'r': builder.Append('\r'); break;
                case 'f': builder.Append('\f'); break;
                case 'u':
                    if (index + 4 >= value.Length)
                        throw new FormatException("A Unicode property escape is incomplete.");

                    var hex = value.Substring(index + 1, 4);
                    if (!ushort.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var codePoint))
                        throw new FormatException($"Unicode property escape '\\u{hex}' is invalid.");

                    builder.Append((char)codePoint);
                    index += 4;
                    break;
                default:
                    builder.Append(current);
                    break;
            }
        }

        return builder.ToString();
    }

    private static bool EndsWithContinuation(StringBuilder line)
    {
        var slashCount = 0;
        for (var index = line.Length - 1; index >= 0 && line[index] == '\\'; index--)
            slashCount++;
        return slashCount % 2 == 1;
    }

    private static bool TryExtractVersion(string distributionUrl, out string? version, out GradleDistributionType distributionType)
    {
        version = null;
        distributionType = GradleDistributionType.Unknown;

        string fileName;
        if (Uri.TryCreate(distributionUrl, UriKind.Absolute, out var uri))
        {
            fileName = Path.GetFileName(Uri.UnescapeDataString(uri.AbsolutePath));
        }
        else
        {
            var withoutFragment = distributionUrl.Split('#', 2)[0];
            var withoutQuery = withoutFragment.Split('?', 2)[0];
            fileName = Path.GetFileName(withoutQuery.Replace('/', Path.DirectorySeparatorChar));
        }

        var match = DistributionFilePattern.Match(fileName);
        if (!match.Success)
            return false;

        version = match.Groups["version"].Value.Trim();
        if (version.Length == 0)
            return false;

        distributionType = match.Groups["type"].Value.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? GradleDistributionType.All
            : GradleDistributionType.Bin;
        return true;
    }

    private static string SanitizeUrlEvidence(string value)
    {
        var trimmed = value.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Scheme))
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

        var queryIndex = trimmed.IndexOfAny(new[] { '?', '#' });
        var withoutQuery = queryIndex >= 0 ? trimmed[..queryIndex] : trimmed;
        var atIndex = withoutQuery.IndexOf('@');
        return atIndex > 0 && withoutQuery[..atIndex].Contains(':')
            ? "[redacted]" + withoutQuery[atIndex..]
            : withoutQuery;
    }

    private static bool IsReparsePoint(string path)
        => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static GradleWrapperParseResult Result(
        GradleWrapperParseStatus status,
        FlutterProjectRootResult projectRoot,
        string? propertiesPath,
        string? distributionUrl,
        string? gradleVersion,
        GradleDistributionType distributionType,
        string? rawText,
        string message)
        => new(status, projectRoot, propertiesPath, distributionUrl, gradleVersion, distributionType, rawText, message);
}
