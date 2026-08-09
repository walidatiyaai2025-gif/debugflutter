using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Flutter.ProjectAnalysis;

public sealed partial class KotlinPluginVersionParser : IKotlinPluginVersionParser
{
    private const long MaxScriptBytes = 512 * 1024;

    public KotlinPluginVersionResult Parse(GradleDslDetectionResult gradleDsl)
    {
        ArgumentNullException.ThrowIfNull(gradleDsl);

        if (!gradleDsl.IsSuccess || string.IsNullOrWhiteSpace(gradleDsl.AndroidDirectory))
        {
            return Result(
                KotlinPluginVersionStatus.GradleDslUnavailable,
                gradleDsl,
                null,
                Array.Empty<KotlinPluginVersionEvidence>(),
                "A successful FBD-604 Gradle DSL result is required before Kotlin plugin version parsing.");
        }

        if (gradleDsl.Scripts.Count == 0)
        {
            return Result(
                KotlinPluginVersionStatus.ScriptUnavailable,
                gradleDsl,
                null,
                Array.Empty<KotlinPluginVersionEvidence>(),
                "No Gradle scripts are available for Kotlin plugin version inspection.");
        }

        string androidDirectory;
        try
        {
            androidDirectory = Path.GetFullPath(gradleDsl.AndroidDirectory);
            if (!Directory.Exists(androidDirectory) || IsReparsePoint(androidDirectory))
            {
                return Result(
                    KotlinPluginVersionStatus.UnsafePath,
                    gradleDsl,
                    null,
                    Array.Empty<KotlinPluginVersionEvidence>(),
                    "The Android project directory is missing or is now a reparse point/symbolic link.");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Result(
                KotlinPluginVersionStatus.UnsafePath,
                gradleDsl,
                null,
                Array.Empty<KotlinPluginVersionEvidence>(),
                $"Android project boundary inspection failed: {ex.Message}");
        }

        var evidence = new List<KotlinPluginVersionEvidence>();
        var legacyPropertyCandidates = new List<KotlinPluginVersionEvidence>();
        var declarationSeen = false;
        var legacyGradlePluginSeen = false;

        foreach (var script in gradleDsl.Scripts)
        {
            string scriptPath;
            string expectedPath;
            try
            {
                scriptPath = Path.GetFullPath(script.Path);
                expectedPath = ExpectedScriptPath(androidDirectory, script.Role, script.Dsl);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return Result(
                    KotlinPluginVersionStatus.UnsafePath,
                    gradleDsl,
                    null,
                    evidence.ToArray(),
                    $"Gradle script path is invalid: {ex.Message}");
            }

            if (!PathsEqual(scriptPath, expectedPath))
            {
                return Result(
                    KotlinPluginVersionStatus.UnsafePath,
                    gradleDsl,
                    null,
                    evidence.ToArray(),
                    $"FBD-604 supplied a {script.Role} script outside its expected Android Gradle location.");
            }

            string text;
            try
            {
                if (script.Role == GradleScriptRole.AppBuild)
                {
                    var appDirectory = Path.Combine(androidDirectory, "app");
                    if (!Directory.Exists(appDirectory) || IsReparsePoint(appDirectory))
                    {
                        return Result(
                            KotlinPluginVersionStatus.UnsafePath,
                            gradleDsl,
                            null,
                            evidence.ToArray(),
                            "The Android app directory is missing or is now a reparse point/symbolic link.");
                    }
                }

                if (!File.Exists(scriptPath))
                {
                    return Result(
                        KotlinPluginVersionStatus.ScriptUnavailable,
                        gradleDsl,
                        null,
                        evidence.ToArray(),
                        $"Gradle script evidence is stale because '{Path.GetFileName(scriptPath)}' is no longer available.");
                }

                if (IsReparsePoint(scriptPath))
                {
                    return Result(
                        KotlinPluginVersionStatus.UnsafePath,
                        gradleDsl,
                        null,
                        evidence.ToArray(),
                        $"Gradle script '{Path.GetFileName(scriptPath)}' is a reparse point or symbolic link and was not followed.");
                }

                if (new FileInfo(scriptPath).Length > MaxScriptBytes)
                {
                    return Result(
                        KotlinPluginVersionStatus.FileTooLarge,
                        gradleDsl,
                        null,
                        evidence.ToArray(),
                        $"Gradle script '{Path.GetFileName(scriptPath)}' exceeds the {MaxScriptBytes} byte inspection limit.");
                }

                text = File.ReadAllText(scriptPath, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                return Result(
                    KotlinPluginVersionStatus.ReadFailed,
                    gradleDsl,
                    null,
                    evidence.ToArray(),
                    $"Gradle script '{Path.GetFileName(scriptPath)}' could not be read: {ex.Message}");
            }

            var code = RemoveComments(text);
            declarationSeen |= KotlinDeclarationMarkerRegex().IsMatch(code);
            legacyGradlePluginSeen |= LegacyGradlePluginMarkerRegex().IsMatch(code);

            foreach (Match match in ModernPluginRegex().Matches(code))
            {
                evidence.Add(new KotlinPluginVersionEvidence(
                    match.Groups["version"].Value,
                    KotlinPluginDeclarationKind.ModernPluginDsl,
                    script.Role,
                    scriptPath,
                    match.Groups["plugin"].Value));
            }

            foreach (Match match in KotlinShorthandRegex().Matches(code))
            {
                evidence.Add(new KotlinPluginVersionEvidence(
                    match.Groups["version"].Value,
                    KotlinPluginDeclarationKind.KotlinDslShorthand,
                    script.Role,
                    scriptPath,
                    "kotlin(" + match.Groups["plugin"].Value + ")"));
            }

            foreach (Match match in LegacyClasspathLiteralRegex().Matches(code))
            {
                evidence.Add(new KotlinPluginVersionEvidence(
                    match.Groups["version"].Value,
                    KotlinPluginDeclarationKind.LegacyBuildscriptClasspath,
                    script.Role,
                    scriptPath,
                    "org.jetbrains.kotlin:kotlin-gradle-plugin"));
            }

            foreach (Match match in LegacyVersionPropertyRegex().Matches(code))
            {
                legacyPropertyCandidates.Add(new KotlinPluginVersionEvidence(
                    match.Groups["version"].Value,
                    KotlinPluginDeclarationKind.LegacyVersionProperty,
                    script.Role,
                    scriptPath,
                    "kotlin_version"));
            }

            foreach (Match match in KotlinExtraPropertyRegex().Matches(code))
            {
                legacyPropertyCandidates.Add(new KotlinPluginVersionEvidence(
                    match.Groups["version"].Value,
                    KotlinPluginDeclarationKind.LegacyVersionProperty,
                    script.Role,
                    scriptPath,
                    "kotlin_version"));
            }
        }

        if (legacyGradlePluginSeen)
            evidence.AddRange(legacyPropertyCandidates);

        var versions = evidence
            .Select(item => item.Version)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(version => version, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (versions.Length == 0)
        {
            return Result(
                KotlinPluginVersionStatus.VersionNotFound,
                gradleDsl,
                null,
                evidence,
                declarationSeen
                    ? "Kotlin Gradle plugin declarations were found, but no literal Kotlin plugin version could be resolved. Variables, aliases, and version catalogs are not guessed."
                    : "No supported explicit Kotlin Gradle plugin version declaration was found in the detected Gradle scripts.");
        }

        if (versions.Length > 1)
        {
            return Result(
                KotlinPluginVersionStatus.Ambiguous,
                gradleDsl,
                null,
                evidence,
                $"Conflicting Kotlin Gradle plugin versions were found ({string.Join(", ", versions)}); no version was selected implicitly.");
        }

        return Result(
            KotlinPluginVersionStatus.Succeeded,
            gradleDsl,
            versions[0],
            evidence,
            $"Kotlin Gradle plugin {versions[0]} detected from {evidence.Count} explicit declaration(s) without executing Gradle.");
    }

    private static string ExpectedScriptPath(string androidDirectory, GradleScriptRole role, GradleDslKind dsl)
    {
        var fileName = dsl == GradleDslKind.Kotlin ? "build.gradle.kts" : "build.gradle";
        return role switch
        {
            GradleScriptRole.Settings => Path.GetFullPath(Path.Combine(androidDirectory, dsl == GradleDslKind.Kotlin ? "settings.gradle.kts" : "settings.gradle")),
            GradleScriptRole.ProjectBuild => Path.GetFullPath(Path.Combine(androidDirectory, fileName)),
            GradleScriptRole.AppBuild => Path.GetFullPath(Path.Combine(androidDirectory, "app", fileName)),
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unsupported Gradle script role.")
        };
    }

    private static string RemoveComments(string text)
    {
        var output = new StringBuilder(text.Length);
        var inBlockComment = false;
        char quote = '\0';
        var escaped = false;

        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            var next = index + 1 < text.Length ? text[index + 1] : '\0';

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    inBlockComment = false;
                    output.Append("  ");
                    index++;
                }
                else
                {
                    output.Append(current is '\r' or '\n' ? current : ' ');
                }
                continue;
            }

            if (quote != '\0')
            {
                output.Append(current);
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

                if (current == quote)
                    quote = '\0';
                continue;
            }

            if (current == '/' && next == '/')
            {
                output.Append("  ");
                index++;
                while (index + 1 < text.Length && text[index + 1] is not '\r' and not '\n')
                {
                    output.Append(' ');
                    index++;
                }
                continue;
            }

            if (current == '/' && next == '*')
            {
                inBlockComment = true;
                output.Append("  ");
                index++;
                continue;
            }

            if (current is '\'' or '"')
                quote = current;

            output.Append(current);
        }

        return output.ToString();
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool IsReparsePoint(string path)
        => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static KotlinPluginVersionResult Result(
        KotlinPluginVersionStatus status,
        GradleDslDetectionResult gradleDsl,
        string? version,
        IReadOnlyList<KotlinPluginVersionEvidence> evidence,
        string message)
        => new(status, gradleDsl, version, evidence, message);

    [GeneratedRegex(
        "(?ix)\\bid\\s*(?:\\(\\s*)?[\"'](?<plugin>org\\.jetbrains\\.kotlin(?:\\.[A-Za-z0-9_.-]+)+)[\"']\\s*\\)?\\s*version\\s*(?:\\(\\s*)?[\"'](?<version>[0-9][0-9A-Za-z.+-]*)[\"']\\s*\\)?",
        RegexOptions.CultureInvariant)]
    private static partial Regex ModernPluginRegex();

    [GeneratedRegex(
        "(?ix)\\bkotlin\\s*\\(\\s*[\"'](?<plugin>[A-Za-z0-9_.-]+)[\"']\\s*\\)\\s*version\\s*[\"'](?<version>[0-9][0-9A-Za-z.+-]*)[\"']",
        RegexOptions.CultureInvariant)]
    private static partial Regex KotlinShorthandRegex();

    [GeneratedRegex(
        "(?ix)\\bclasspath\\s*(?:\\(\\s*)?[\"']org\\.jetbrains\\.kotlin\\s*:\\s*kotlin-gradle-plugin\\s*:\\s*(?<version>[0-9][0-9A-Za-z.+-]*)[\"']\\s*\\)?",
        RegexOptions.CultureInvariant)]
    private static partial Regex LegacyClasspathLiteralRegex();

    [GeneratedRegex(
        "(?ix)\\b(?:ext\\.)?kotlin_version\\s*=\\s*[\"'](?<version>[0-9][0-9A-Za-z.+-]*)[\"']",
        RegexOptions.CultureInvariant)]
    private static partial Regex LegacyVersionPropertyRegex();

    [GeneratedRegex(
        "(?ix)\\bextra\\s*\\[\\s*[\"']kotlin_version[\"']\\s*\\]\\s*=\\s*[\"'](?<version>[0-9][0-9A-Za-z.+-]*)[\"']",
        RegexOptions.CultureInvariant)]
    private static partial Regex KotlinExtraPropertyRegex();

    [GeneratedRegex(
        "(?ix)org\\.jetbrains\\.kotlin(?:\\.[A-Za-z0-9_.-]+)+|\\bkotlin\\s*\\(|org\\.jetbrains\\.kotlin\\s*:\\s*kotlin-gradle-plugin|\\bkotlin_version\\b|libs\\.plugins\\.[A-Za-z0-9_.-]*kotlin",
        RegexOptions.CultureInvariant)]
    private static partial Regex KotlinDeclarationMarkerRegex();

    [GeneratedRegex(
        "(?ix)org\\.jetbrains\\.kotlin\\s*:\\s*kotlin-gradle-plugin",
        RegexOptions.CultureInvariant)]
    private static partial Regex LegacyGradlePluginMarkerRegex();
}
