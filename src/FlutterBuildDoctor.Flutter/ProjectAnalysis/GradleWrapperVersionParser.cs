using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Flutter.ProjectAnalysis;

public sealed partial class GradleWrapperVersionParser : IGradleWrapperVersionParser
{
    private const long MaxPropertiesBytes = 256 * 1024;
    private const int MaxLogicalLines = 2048;

    public GradleWrapperVersionResult Parse(FlutterProjectRootResult projectRoot)
    {
        ArgumentNullException.ThrowIfNull(projectRoot);

        if (!projectRoot.IsSuccess || string.IsNullOrWhiteSpace(projectRoot.EffectiveRoot))
        {
            return Result(
                GradleWrapperVersionStatus.ProjectRootUnavailable,
                projectRoot,
                null,
                null,
                null,
                GradleDistributionKind.Unknown,
                null,
                "A successfully resolved Flutter project root is required before Gradle wrapper parsing.");
        }

        string rootPath;
        string androidDirectory;
        string gradleDirectory;
        string wrapperDirectory;
        string propertiesPath;
        try
        {
            rootPath = Path.GetFullPath(projectRoot.EffectiveRoot);
            androidDirectory = Path.Combine(rootPath, "android");
            gradleDirectory = Path.Combine(androidDirectory, "gradle");
            wrapperDirectory = Path.Combine(gradleDirectory, "wrapper");
            propertiesPath = Path.GetFullPath(Path.Combine(wrapperDirectory, "gradle-wrapper.properties"));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Result(
                GradleWrapperVersionStatus.ProjectRootUnavailable,
                projectRoot,
                null,
                null,
                null,
                GradleDistributionKind.Unknown,
                null,
                $"Resolved project path is invalid: {ex.Message}");
        }

        if (!Directory.Exists(androidDirectory))
        {
            return Result(
                GradleWrapperVersionStatus.AndroidDirectoryMissing,
                projectRoot,
                propertiesPath,
                null,
                null,
                GradleDistributionKind.Unknown,
                null,
                "The Flutter project does not contain an Android project directory.");
        }

        try
        {
            foreach (var (path, subject) in new[]
                     {
                         (rootPath, "Flutter project directory"),
                         (androidDirectory, "Android project directory"),
                         (gradleDirectory, "Android Gradle directory"),
                         (wrapperDirectory, "Gradle wrapper directory")
                     })
            {
                if (Directory.Exists(path) && IsReparsePoint(path))
                    return UnsafeBoundary(projectRoot, propertiesPath, subject);
            }

            if (!File.Exists(propertiesPath))
            {
                return Result(
                    GradleWrapperVersionStatus.WrapperPropertiesMissing,
                    projectRoot,
                    propertiesPath,
                    null,
                    null,
                    GradleDistributionKind.Unknown,
                    null,
                    "gradle-wrapper.properties was not found under android/gradle/wrapper. Gradle was not executed or repaired.");
            }

            if (IsReparsePoint(propertiesPath))
                return UnsafeBoundary(projectRoot, propertiesPath, "Gradle wrapper properties file");

            var fileInfo = new FileInfo(propertiesPath);
            if (fileInfo.Length > MaxPropertiesBytes)
            {
                return Result(
                    GradleWrapperVersionStatus.FileTooLarge,
                    projectRoot,
                    propertiesPath,
                    null,
                    null,
                    GradleDistributionKind.Unknown,
                    null,
                    $"gradle-wrapper.properties exceeds the {MaxPropertiesBytes} byte inspection limit.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Result(
                GradleWrapperVersionStatus.InspectionFailed,
                projectRoot,
                propertiesPath,
                null,
                null,
                GradleDistributionKind.Unknown,
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
                GradleWrapperVersionStatus.ReadFailed,
                projectRoot,
                propertiesPath,
                null,
                null,
                GradleDistributionKind.Unknown,
                null,
                $"gradle-wrapper.properties could not be read: {ex.Message}");
        }

        List<string> distributionValues;
        try
        {
            distributionValues = new List<string>();
            foreach (var logicalLine in ReadLogicalLines(rawText))
            {
                if (!TrySplitProperty(logicalLine, out var key, out var rawValue) ||
                    !string.Equals(key, "distributionUrl", StringComparison.Ordinal))
                    continue;

                if (!TryUnescapeJavaProperty(rawValue, out var decoded))
                {
                    return Result(
                        GradleWrapperVersionStatus.InvalidProperties,
                        projectRoot,
                        propertiesPath,
                        null,
                        null,
                        GradleDistributionKind.Unknown,
                        rawText,
                        "distributionUrl contains an invalid Java properties escape sequence.");
                }

                distributionValues.Add(decoded.Trim());
            }
        }
        catch (FormatException ex)
        {
            return Result(
                GradleWrapperVersionStatus.InvalidProperties,
                projectRoot,
                propertiesPath,
                null,
                null,
                GradleDistributionKind.Unknown,
                rawText,
                $"gradle-wrapper.properties is malformed: {ex.Message}");
        }

        if (distributionValues.Count == 0)
        {
            return Result(
                GradleWrapperVersionStatus.DistributionUrlMissing,
                projectRoot,
                propertiesPath,
                null,
                null,
                GradleDistributionKind.Unknown,
                rawText,
                "distributionUrl is missing from gradle-wrapper.properties.");
        }

        var distinctValues = distributionValues
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (distinctValues.Length != 1)
        {
            return Result(
                GradleWrapperVersionStatus.DistributionUrlInvalid,
                projectRoot,
                propertiesPath,
                null,
                null,
                GradleDistributionKind.Unknown,
                rawText,
                distinctValues.Length == 0
                    ? "distributionUrl is empty in gradle-wrapper.properties."
                    : "Multiple different distributionUrl values were found; no wrapper version was selected implicitly.");
        }

        var distributionUrl = distinctValues[0];
        var match = GradleDistributionRegex().Match(distributionUrl);
        if (!match.Success)
        {
            return Result(
                GradleWrapperVersionStatus.VersionNotFound,
                projectRoot,
                propertiesPath,
                SanitizeDistributionUrl(distributionUrl),
                null,
                GradleDistributionKind.Unknown,
                rawText,
                "distributionUrl was read, but a Gradle distribution version could not be parsed from its archive name.");
        }

        var version = match.Groups["version"].Value;
        var kind = string.Equals(match.Groups["kind"].Value, "all", StringComparison.OrdinalIgnoreCase)
            ? GradleDistributionKind.All
            : GradleDistributionKind.Bin;

        return Result(
            GradleWrapperVersionStatus.Succeeded,
            projectRoot,
            propertiesPath,
            SanitizeDistributionUrl(distributionUrl),
            version,
            kind,
            rawText,
            $"Gradle wrapper {version} ({kind.ToString().ToLowerInvariant()}) detected from gradle-wrapper.properties without executing Gradle.");
    }

    private static IEnumerable<string> ReadLogicalLines(string rawText)
    {
        using var reader = new StringReader(rawText);
        var builder = new StringBuilder();
        var logicalLineCount = 0;

        while (reader.ReadLine() is { } physical)
        {
            if (builder.Length == 0)
                builder.Append(physical);
            else
                builder.Append(physical.TrimStart());

            if (HasContinuation(builder))
            {
                builder.Length--;
                continue;
            }

            if (++logicalLineCount > MaxLogicalLines)
                throw new FormatException($"The file exceeds the {MaxLogicalLines} logical-line parsing limit.");

            yield return builder.ToString();
            builder.Clear();
        }

        if (builder.Length > 0)
        {
            if (++logicalLineCount > MaxLogicalLines)
                throw new FormatException($"The file exceeds the {MaxLogicalLines} logical-line parsing limit.");
            yield return builder.ToString();
        }
    }

    private static bool HasContinuation(StringBuilder builder)
    {
        var backslashes = 0;
        for (var index = builder.Length - 1; index >= 0 && builder[index] == '\\'; index--)
            backslashes++;
        return backslashes % 2 == 1;
    }

    private static bool TrySplitProperty(string line, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;

        var span = line.AsSpan().TrimStart();
        if (span.IsEmpty || span[0] is '#' or '!')
            return false;

        var escaped = false;
        var separatorIndex = -1;
        for (var index = 0; index < span.Length; index++)
        {
            var character = span[index];
            if (!escaped && (character is '=' or ':' || char.IsWhiteSpace(character)))
            {
                separatorIndex = index;
                break;
            }

            escaped = character == '\\' && !escaped;
            if (character != '\\')
                escaped = false;
        }

        if (separatorIndex < 0)
        {
            key = span.ToString().Trim();
            return key.Length > 0;
        }

        key = span[..separatorIndex].ToString().Trim();
        var valueStart = separatorIndex;
        while (valueStart < span.Length && char.IsWhiteSpace(span[valueStart]))
            valueStart++;
        if (valueStart < span.Length && span[valueStart] is '=' or ':')
            valueStart++;
        while (valueStart < span.Length && char.IsWhiteSpace(span[valueStart]))
            valueStart++;

        value = span[valueStart..].ToString();
        return key.Length > 0;
    }

    private static bool TryUnescapeJavaProperty(string value, out string decoded)
    {
        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character != '\\')
            {
                builder.Append(character);
                continue;
            }

            if (++index >= value.Length)
            {
                decoded = string.Empty;
                return false;
            }

            character = value[index];
            switch (character)
            {
                case 't': builder.Append('\t'); break;
                case 'r': builder.Append('\r'); break;
                case 'n': builder.Append('\n'); break;
                case 'f': builder.Append('\f'); break;
                case 'u':
                    if (index + 4 >= value.Length ||
                        !ushort.TryParse(value.AsSpan(index + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var codePoint))
                    {
                        decoded = string.Empty;
                        return false;
                    }
                    builder.Append((char)codePoint);
                    index += 4;
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        decoded = builder.ToString();
        return true;
    }

    private static string SanitizeDistributionUrl(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
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

        var secretStart = value.IndexOfAny(new[] { '?', '#' });
        var withoutSecrets = secretStart >= 0 ? value[..secretStart] : value;
        var atIndex = withoutSecrets.IndexOf('@');
        return atIndex > 0 && withoutSecrets[..atIndex].Contains(':')
            ? "[redacted]" + withoutSecrets[atIndex..]
            : withoutSecrets;
    }

    private static bool IsReparsePoint(string path)
        => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static GradleWrapperVersionResult UnsafeBoundary(
        FlutterProjectRootResult projectRoot,
        string? propertiesPath,
        string subject)
        => Result(
            GradleWrapperVersionStatus.UnsafePath,
            projectRoot,
            propertiesPath,
            null,
            null,
            GradleDistributionKind.Unknown,
            null,
            $"{subject} is a reparse point or symbolic link and will not be followed outside the resolved Flutter project boundary.");

    private static GradleWrapperVersionResult Result(
        GradleWrapperVersionStatus status,
        FlutterProjectRootResult projectRoot,
        string? propertiesPath,
        string? distributionUrl,
        string? version,
        GradleDistributionKind distributionKind,
        string? rawText,
        string message)
        => new(status, projectRoot, propertiesPath, distributionUrl, version, distributionKind, rawText, message);

    [GeneratedRegex(@"(?i)(?:^|[/\\])gradle-(?<version>[0-9][0-9A-Za-z.+-]*)-(?<kind>all|bin)\.zip(?:$|[?#])", RegexOptions.CultureInvariant)]
    private static partial Regex GradleDistributionRegex();
}
