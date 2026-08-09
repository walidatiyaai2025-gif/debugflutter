using System.Globalization;
using System.IO;
using System.Text;

namespace FlutterBuildDoctor.Flutter.ProjectAnalysis;

public sealed class LocalPropertiesDetector : ILocalPropertiesDetector
{
    private const long MaxFileBytes = 256 * 1024;
    private const int MaxRelevantOccurrences = 32;

    private const string AndroidSdkKey = "sdk.dir";
    private const string FlutterSdkKey = "flutter.sdk";

    public LocalPropertiesDetectionResult Detect(FlutterProjectRootResult projectRoot)
    {
        ArgumentNullException.ThrowIfNull(projectRoot);

        if (!projectRoot.IsSuccess || string.IsNullOrWhiteSpace(projectRoot.EffectiveRoot))
        {
            return BoundaryResult(
                LocalPropertiesDetectionStatus.ProjectRootUnavailable,
                projectRoot,
                null,
                null,
                "A successful FBD-601 Flutter project root is required before local.properties detection.");
        }

        string root;
        string androidDirectory;
        string localPropertiesPath;
        try
        {
            root = Path.GetFullPath(projectRoot.EffectiveRoot);
            androidDirectory = Path.GetFullPath(Path.Combine(root, "android"));
            localPropertiesPath = Path.GetFullPath(Path.Combine(androidDirectory, "local.properties"));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return BoundaryResult(
                LocalPropertiesDetectionStatus.UnsafePath,
                projectRoot,
                null,
                null,
                $"Project/android/local.properties path is invalid: {ex.Message}");
        }

        if (!IsWithinPath(androidDirectory, root) || !IsWithinPath(localPropertiesPath, androidDirectory))
        {
            return BoundaryResult(
                LocalPropertiesDetectionStatus.UnsafePath,
                projectRoot,
                androidDirectory,
                localPropertiesPath,
                "The computed android/local.properties path escapes the FBD-601 project boundary.");
        }

        try
        {
            if (!Directory.Exists(root))
            {
                return BoundaryResult(
                    LocalPropertiesDetectionStatus.ProjectRootUnavailable,
                    projectRoot,
                    androidDirectory,
                    localPropertiesPath,
                    "The FBD-601 project root is stale because the directory no longer exists.");
            }

            if (IsReparsePoint(root))
            {
                return BoundaryResult(
                    LocalPropertiesDetectionStatus.UnsafePath,
                    projectRoot,
                    androidDirectory,
                    localPropertiesPath,
                    "The Flutter project root is a reparse point/symbolic link and was not traversed.");
            }

            if (!Directory.Exists(androidDirectory))
            {
                return BoundaryResult(
                    LocalPropertiesDetectionStatus.AndroidDirectoryUnavailable,
                    projectRoot,
                    androidDirectory,
                    localPropertiesPath,
                    "The Flutter project does not currently contain an android directory.");
            }

            if (IsReparsePoint(androidDirectory))
            {
                return BoundaryResult(
                    LocalPropertiesDetectionStatus.UnsafePath,
                    projectRoot,
                    androidDirectory,
                    localPropertiesPath,
                    "The Android project directory is a reparse point/symbolic link and was not traversed.");
            }

            if (!File.Exists(localPropertiesPath))
            {
                return new LocalPropertiesDetectionResult(
                    LocalPropertiesDetectionStatus.FileMissing,
                    projectRoot,
                    androidDirectory,
                    localPropertiesPath,
                    Missing(LocalPropertiesPathKind.AndroidSdk, AndroidSdkKey, "android/local.properties is missing."),
                    Missing(LocalPropertiesPathKind.FlutterSdk, FlutterSdkKey, "android/local.properties is missing."),
                    "android/local.properties is not present. SDK paths were not inferred from other project files.");
            }

            if (IsReparsePoint(localPropertiesPath))
            {
                return BoundaryResult(
                    LocalPropertiesDetectionStatus.UnsafePath,
                    projectRoot,
                    androidDirectory,
                    localPropertiesPath,
                    "android/local.properties is a reparse point/symbolic link and was not read.");
            }

            if (new FileInfo(localPropertiesPath).Length > MaxFileBytes)
            {
                return BoundaryResult(
                    LocalPropertiesDetectionStatus.FileTooLarge,
                    projectRoot,
                    androidDirectory,
                    localPropertiesPath,
                    $"android/local.properties exceeds the {MaxFileBytes} byte inspection limit.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return BoundaryResult(
                LocalPropertiesDetectionStatus.ReadFailed,
                projectRoot,
                androidDirectory,
                localPropertiesPath,
                $"android/local.properties metadata could not be inspected: {ex.Message}");
        }

        string text;
        try
        {
            text = File.ReadAllText(localPropertiesPath, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return BoundaryResult(
                LocalPropertiesDetectionStatus.ReadFailed,
                projectRoot,
                androidDirectory,
                localPropertiesPath,
                $"android/local.properties could not be read: {ex.Message}");
        }

        var observations = ParseRelevantObservations(text);
        var androidSdk = BuildPathResult(
            LocalPropertiesPathKind.AndroidSdk,
            AndroidSdkKey,
            observations.Where(item => item.Kind == LocalPropertiesPathKind.AndroidSdk).ToArray(),
            ValidateAndroidSdkPath);
        var flutterSdk = BuildPathResult(
            LocalPropertiesPathKind.FlutterSdk,
            FlutterSdkKey,
            observations.Where(item => item.Kind == LocalPropertiesPathKind.FlutterSdk).ToArray(),
            ValidateFlutterSdkPath);

        var status = androidSdk.Status == LocalPropertiesPathStatus.Ambiguous ||
                     flutterSdk.Status == LocalPropertiesPathStatus.Ambiguous
            ? LocalPropertiesDetectionStatus.Ambiguous
            : androidSdk.IsValid && flutterSdk.IsValid
                ? LocalPropertiesDetectionStatus.Succeeded
                : LocalPropertiesDetectionStatus.Partial;

        var message = status switch
        {
            LocalPropertiesDetectionStatus.Succeeded =>
                "Android SDK and Flutter SDK paths were parsed from android/local.properties and validated without executing tooling.",
            LocalPropertiesDetectionStatus.Ambiguous =>
                "Conflicting relevant local.properties values were found. No ambiguous SDK path was selected implicitly.",
            _ =>
                "android/local.properties was parsed, but one or more SDK path requirements are missing or invalid."
        };

        return new LocalPropertiesDetectionResult(
            status,
            projectRoot,
            androidDirectory,
            localPropertiesPath,
            androidSdk,
            flutterSdk,
            message);
    }

    private static IReadOnlyList<RelevantObservation> ParseRelevantObservations(string text)
    {
        var results = new List<RelevantObservation>();
        var occurrences = new Dictionary<LocalPropertiesPathKind, int>();

        foreach (var logicalLine in EnumerateLogicalLines(text))
        {
            if (!TrySplitProperty(logicalLine, out var rawKey, out var rawValue))
                continue;

            if (!TryDecodePropertyToken(rawKey, out var key))
                continue;

            LocalPropertiesPathKind kind;
            if (string.Equals(key, AndroidSdkKey, StringComparison.Ordinal))
                kind = LocalPropertiesPathKind.AndroidSdk;
            else if (string.Equals(key, FlutterSdkKey, StringComparison.Ordinal))
                kind = LocalPropertiesPathKind.FlutterSdk;
            else
                continue;

            var occurrence = occurrences.TryGetValue(kind, out var previous)
                ? previous + 1
                : 1;
            occurrences[kind] = occurrence;

            if (occurrence > MaxRelevantOccurrences)
            {
                results.Add(new RelevantObservation(
                    kind,
                    key,
                    null,
                    "The relevant property occurs more often than the bounded parser limit permits.",
                    occurrence));
                continue;
            }

            if (!TryDecodePropertyToken(rawValue, out var decodedValue))
            {
                results.Add(new RelevantObservation(
                    kind,
                    key,
                    null,
                    "The relevant property value contains an invalid Java-properties escape sequence.",
                    occurrence));
                continue;
            }

            results.Add(new RelevantObservation(kind, key, decodedValue, null, occurrence));
        }

        return results;
    }

    private static LocalPropertiesPathResult BuildPathResult(
        LocalPropertiesPathKind kind,
        string key,
        IReadOnlyList<RelevantObservation> observations,
        Func<string, PathValidation> validator)
    {
        if (observations.Count == 0)
            return Missing(kind, key, $"Property '{key}' is not configured in android/local.properties.");

        var evidence = observations
            .Where(item => item.DecodedValue is not null)
            .Select(item => new LocalPropertiesPathEvidence(
                kind,
                key,
                item.DecodedValue!,
                TryNormalizePath(item.DecodedValue!, out var normalized, out _) ? normalized : null,
                item.Occurrence))
            .ToArray();

        var hasMalformedOccurrence = observations.Any(item => item.Error is not null);
        var distinctValues = observations
            .Where(item => item.DecodedValue is not null)
            .Select(item => item.DecodedValue!)
            .Distinct(PathValueComparer())
            .ToArray();

        if (hasMalformedOccurrence && observations.Count > 1 || distinctValues.Length > 1)
        {
            return new LocalPropertiesPathResult(
                kind,
                key,
                LocalPropertiesPathStatus.Ambiguous,
                null,
                null,
                Exists: false,
                HasExpectedLayout: false,
                observations.Count,
                evidence,
                $"Property '{key}' has conflicting or unparseable duplicate occurrences. No value was selected.");
        }

        if (hasMalformedOccurrence)
        {
            return new LocalPropertiesPathResult(
                kind,
                key,
                LocalPropertiesPathStatus.InvalidPath,
                null,
                null,
                Exists: false,
                HasExpectedLayout: false,
                observations.Count,
                evidence,
                observations.First(item => item.Error is not null).Error!);
        }

        var configured = distinctValues.Single();
        if (string.IsNullOrWhiteSpace(configured.Trim().Trim('"')))
        {
            return new LocalPropertiesPathResult(
                kind,
                key,
                LocalPropertiesPathStatus.EmptyValue,
                configured,
                null,
                Exists: false,
                HasExpectedLayout: false,
                observations.Count,
                evidence,
                $"Property '{key}' is configured but empty.");
        }

        if (!TryNormalizePath(configured, out var normalizedPath, out var normalizationError))
        {
            return new LocalPropertiesPathResult(
                kind,
                key,
                LocalPropertiesPathStatus.InvalidPath,
                configured,
                null,
                Exists: false,
                HasExpectedLayout: false,
                observations.Count,
                evidence,
                normalizationError ?? $"Property '{key}' could not be normalized as a local path.");
        }

        var validation = validator(normalizedPath!);
        return new LocalPropertiesPathResult(
            kind,
            key,
            validation.Status,
            configured,
            normalizedPath,
            validation.Exists,
            validation.HasExpectedLayout,
            observations.Count,
            evidence,
            validation.Message);
    }

    private static PathValidation ValidateAndroidSdkPath(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return new PathValidation(
                    LocalPropertiesPathStatus.DirectoryMissing,
                    Exists: false,
                    HasExpectedLayout: false,
                    "The configured Android SDK directory does not exist.");
            }

            var recognized = Directory.Exists(Path.Combine(path, "platform-tools")) ||
                             Directory.Exists(Path.Combine(path, "platforms")) ||
                             Directory.Exists(Path.Combine(path, "build-tools")) ||
                             Directory.Exists(Path.Combine(path, "cmdline-tools")) ||
                             Directory.Exists(Path.Combine(path, "tools")) ||
                             Directory.Exists(Path.Combine(path, "licenses")) ||
                             Directory.Exists(Path.Combine(path, "emulator"));

            return recognized
                ? new PathValidation(
                    LocalPropertiesPathStatus.Valid,
                    Exists: true,
                    HasExpectedLayout: true,
                    "The configured Android SDK directory exists and contains a recognized SDK layout marker.")
                : new PathValidation(
                    LocalPropertiesPathStatus.UnrecognizedLayout,
                    Exists: true,
                    HasExpectedLayout: false,
                    "The configured directory exists but does not contain a recognized Android SDK layout marker.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new PathValidation(
                LocalPropertiesPathStatus.InvalidPath,
                Exists: false,
                HasExpectedLayout: false,
                $"The configured Android SDK path could not be inspected: {ex.Message}");
        }
    }

    private static PathValidation ValidateFlutterSdkPath(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return new PathValidation(
                    LocalPropertiesPathStatus.DirectoryMissing,
                    Exists: false,
                    HasExpectedLayout: false,
                    "The configured Flutter SDK directory does not exist.");
            }

            var bin = Path.Combine(path, "bin");
            var launcher = File.Exists(Path.Combine(bin, "flutter.bat")) ||
                           File.Exists(Path.Combine(bin, "flutter"));
            var recognized = Directory.Exists(bin) && launcher;

            return recognized
                ? new PathValidation(
                    LocalPropertiesPathStatus.Valid,
                    Exists: true,
                    HasExpectedLayout: true,
                    "The configured Flutter SDK directory exists and contains the expected Flutter launcher under bin.")
                : new PathValidation(
                    LocalPropertiesPathStatus.UnrecognizedLayout,
                    Exists: true,
                    HasExpectedLayout: false,
                    "The configured directory exists but does not contain an expected Flutter SDK bin/flutter launcher.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new PathValidation(
                LocalPropertiesPathStatus.InvalidPath,
                Exists: false,
                HasExpectedLayout: false,
                $"The configured Flutter SDK path could not be inspected: {ex.Message}");
        }
    }

    private static bool TryNormalizePath(string configuredValue, out string? normalizedPath, out string? error)
    {
        normalizedPath = null;
        error = null;

        var trimmed = configuredValue.Trim().Trim('"');
        if (trimmed.Length == 0)
        {
            error = "The configured path is empty after trimming quotes and whitespace.";
            return false;
        }

        if (trimmed.StartsWith("\\\\", StringComparison.Ordinal))
        {
            error = "UNC/network SDK paths are not probed automatically.";
            return false;
        }

        try
        {
            if (!Path.IsPathFullyQualified(trimmed))
            {
                error = "The configured SDK path is not fully qualified.";
                return false;
            }

            var fullPath = Path.GetFullPath(trimmed);
            var root = Path.GetPathRoot(fullPath);
            if (!string.IsNullOrEmpty(root) && fullPath.Length > root.Length)
                fullPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            normalizedPath = fullPath;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"The configured SDK path could not be normalized: {ex.Message}";
            return false;
        }
    }

    private static IEnumerable<string> EnumerateLogicalLines(string text)
    {
        using var reader = new StringReader(text);
        StringBuilder? current = null;

        while (reader.ReadLine() is { } line)
        {
            if (current is null)
                current = new StringBuilder();
            else
                line = line.TrimStart(' ', '\t', '\f');

            current.Append(line);
            if (HasContinuation(current))
            {
                current.Length--;
                continue;
            }

            yield return current.ToString();
            current = null;
        }

        if (current is { Length: > 0 })
            yield return current.ToString();
    }

    private static bool HasContinuation(StringBuilder value)
    {
        var slashCount = 0;
        for (var index = value.Length - 1; index >= 0 && value[index] == '\\'; index--)
            slashCount++;

        return slashCount % 2 == 1;
    }

    private static bool TrySplitProperty(string line, out string rawKey, out string rawValue)
    {
        rawKey = string.Empty;
        rawValue = string.Empty;

        var start = 0;
        while (start < line.Length && IsPropertyWhitespace(line[start]))
            start++;

        if (start >= line.Length || line[start] is '#' or '!')
            return false;

        var escaped = false;
        var keyEnd = line.Length;
        var separatorIndex = -1;

        for (var index = start; index < line.Length; index++)
        {
            var current = line[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (current == '\\')
            {
                escaped = true;
                continue;
            }

            if (current is '=' or ':' || IsPropertyWhitespace(current))
            {
                keyEnd = index;
                separatorIndex = index;
                break;
            }
        }

        rawKey = line[start..keyEnd];
        if (separatorIndex < 0)
            return rawKey.Length > 0;

        var valueStart = separatorIndex;
        while (valueStart < line.Length && IsPropertyWhitespace(line[valueStart]))
            valueStart++;

        if (valueStart < line.Length && line[valueStart] is '=' or ':')
            valueStart++;

        while (valueStart < line.Length && IsPropertyWhitespace(line[valueStart]))
            valueStart++;

        rawValue = valueStart < line.Length ? line[valueStart..] : string.Empty;
        return rawKey.Length > 0;
    }

    private static bool TryDecodePropertyToken(string raw, out string decoded)
    {
        var output = new StringBuilder(raw.Length);

        for (var index = 0; index < raw.Length; index++)
        {
            var current = raw[index];
            if (current != '\\')
            {
                output.Append(current);
                continue;
            }

            if (++index >= raw.Length)
            {
                decoded = string.Empty;
                return false;
            }

            current = raw[index];
            switch (current)
            {
                case 't': output.Append('\t'); break;
                case 'n': output.Append('\n'); break;
                case 'r': output.Append('\r'); break;
                case 'f': output.Append('\f'); break;
                case 'u':
                    if (index + 4 >= raw.Length)
                    {
                        decoded = string.Empty;
                        return false;
                    }

                    var hex = raw.AsSpan(index + 1, 4);
                    if (!int.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var codePoint))
                    {
                        decoded = string.Empty;
                        return false;
                    }

                    output.Append((char)codePoint);
                    index += 4;
                    break;
                default:
                    // java.util.Properties semantics: an unknown escape removes the backslash
                    // and keeps the following character (for example \: and \=).
                    output.Append(current);
                    break;
            }
        }

        decoded = output.ToString();
        return true;
    }

    private static bool IsPropertyWhitespace(char value)
        => value is ' ' or '\t' or '\f';

    private static StringComparer PathValueComparer()
        => OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static LocalPropertiesPathResult Missing(
        LocalPropertiesPathKind kind,
        string key,
        string message)
        => new(
            kind,
            key,
            LocalPropertiesPathStatus.MissingKey,
            null,
            null,
            Exists: false,
            HasExpectedLayout: false,
            OccurrenceCount: 0,
            Evidence: Array.Empty<LocalPropertiesPathEvidence>(),
            Message: message);

    private static LocalPropertiesDetectionResult BoundaryResult(
        LocalPropertiesDetectionStatus status,
        FlutterProjectRootResult projectRoot,
        string? androidDirectory,
        string? localPropertiesPath,
        string message)
        => new(
            status,
            projectRoot,
            androidDirectory,
            localPropertiesPath,
            Missing(LocalPropertiesPathKind.AndroidSdk, AndroidSdkKey, "SDK property was not inspected because the local.properties boundary check failed."),
            Missing(LocalPropertiesPathKind.FlutterSdk, FlutterSdkKey, "SDK property was not inspected because the local.properties boundary check failed."),
            message);

    private static bool IsWithinPath(string candidate, string parent)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        var normalizedParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));

        if (string.Equals(normalizedCandidate, normalizedParent, comparison))
            return true;

        return normalizedCandidate.StartsWith(
            normalizedParent + Path.DirectorySeparatorChar,
            comparison);
    }

    private static bool IsReparsePoint(string path)
        => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private readonly record struct RelevantObservation(
        LocalPropertiesPathKind Kind,
        string PropertyKey,
        string? DecodedValue,
        string? Error,
        int Occurrence);

    private readonly record struct PathValidation(
        LocalPropertiesPathStatus Status,
        bool Exists,
        bool HasExpectedLayout,
        string Message);
}
