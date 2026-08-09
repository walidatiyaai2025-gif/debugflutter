using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Flutter.ProjectAnalysis;

public sealed partial class AndroidGradlePluginVersionParser : IAndroidGradlePluginVersionParser
{
    private const long MaxScriptBytes = 512 * 1024;

    public AndroidGradlePluginVersionResult Parse(GradleDslDetectionResult gradleDsl)
    {
        ArgumentNullException.ThrowIfNull(gradleDsl);

        if (!gradleDsl.IsSuccess)
        {
            return Result(
                AndroidGradlePluginVersionStatus.GradleDslUnavailable,
                gradleDsl,
                null,
                Array.Empty<AndroidGradlePluginVersionEvidence>(),
                "A successful FBD-604 Gradle DSL result is required before AGP version parsing.");
        }

        if (gradleDsl.Scripts.Count == 0)
        {
            return Result(
                AndroidGradlePluginVersionStatus.ScriptUnavailable,
                gradleDsl,
                null,
                Array.Empty<AndroidGradlePluginVersionEvidence>(),
                "No Gradle scripts are available for AGP version inspection.");
        }

        var evidence = new List<AndroidGradlePluginVersionEvidence>();
        foreach (var script in gradleDsl.Scripts)
        {
            string text;
            try
            {
                if (!File.Exists(script.Path))
                {
                    return Result(
                        AndroidGradlePluginVersionStatus.ScriptUnavailable,
                        gradleDsl,
                        null,
                        evidence,
                        $"Gradle script evidence is stale because '{Path.GetFileName(script.Path)}' is no longer available.");
                }

                if ((File.GetAttributes(script.Path) & FileAttributes.ReparsePoint) != 0)
                {
                    return Result(
                        AndroidGradlePluginVersionStatus.UnsafePath,
                        gradleDsl,
                        null,
                        evidence,
                        $"Gradle script '{Path.GetFileName(script.Path)}' is a reparse point or symbolic link and was not followed.");
                }

                if (new FileInfo(script.Path).Length > MaxScriptBytes)
                {
                    return Result(
                        AndroidGradlePluginVersionStatus.FileTooLarge,
                        gradleDsl,
                        null,
                        evidence,
                        $"Gradle script '{Path.GetFileName(script.Path)}' exceeds the {MaxScriptBytes} byte inspection limit.");
                }

                text = File.ReadAllText(script.Path, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                return Result(
                    AndroidGradlePluginVersionStatus.ReadFailed,
                    gradleDsl,
                    null,
                    evidence,
                    $"Gradle script '{Path.GetFileName(script.Path)}' could not be read: {ex.Message}");
            }

            var code = RemoveComments(text);
            foreach (Match match in ModernPluginRegex().Matches(code))
            {
                evidence.Add(new AndroidGradlePluginVersionEvidence(
                    match.Groups["version"].Value,
                    AndroidGradlePluginDeclarationKind.ModernPluginDsl,
                    script.Role,
                    script.Path,
                    match.Groups["plugin"].Value));
            }

            foreach (Match match in LegacyClasspathRegex().Matches(code))
            {
                evidence.Add(new AndroidGradlePluginVersionEvidence(
                    match.Groups["version"].Value,
                    AndroidGradlePluginDeclarationKind.LegacyBuildscriptClasspath,
                    script.Role,
                    script.Path,
                    null));
            }
        }

        if (evidence.Count == 0)
        {
            return Result(
                AndroidGradlePluginVersionStatus.VersionNotFound,
                gradleDsl,
                null,
                evidence,
                "No explicit Android Gradle Plugin version was found in the detected Gradle scripts. Dynamic variables or version-catalog aliases are not guessed.");
        }

        var versions = evidence
            .Select(item => item.Version)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(version => version, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (versions.Length != 1)
        {
            return Result(
                AndroidGradlePluginVersionStatus.Ambiguous,
                gradleDsl,
                null,
                evidence,
                $"Conflicting Android Gradle Plugin versions were found ({string.Join(", ", versions)}); no version was selected implicitly.");
        }

        return Result(
            AndroidGradlePluginVersionStatus.Succeeded,
            gradleDsl,
            versions[0],
            evidence,
            $"Android Gradle Plugin {versions[0]} detected from {evidence.Count} explicit declaration(s) without executing Gradle.");
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
                    index++;
                }
                else if (current is '\r' or '\n')
                {
                    output.Append(current);
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
                while (index < text.Length && text[index] is not '\r' and not '\n')
                    index++;
                if (index < text.Length)
                    output.Append(text[index]);
                continue;
            }

            if (current == '/' && next == '*')
            {
                inBlockComment = true;
                index++;
                continue;
            }

            if (current is '\'' or '"')
                quote = current;

            output.Append(current);
        }

        return output.ToString();
    }

    private static AndroidGradlePluginVersionResult Result(
        AndroidGradlePluginVersionStatus status,
        GradleDslDetectionResult gradleDsl,
        string? version,
        IReadOnlyList<AndroidGradlePluginVersionEvidence> evidence,
        string message)
        => new(status, gradleDsl, version, evidence, message);

    [GeneratedRegex(
        "(?ix)\\bid\\s*(?:\\(\\s*)?[\"'](?<plugin>com\\.android\\.(?:application|library|test|dynamic-feature|asset-pack|asset-pack-bundle|lint))[\"']\\s*\\)?\\s*version\\s*(?:\\(\\s*)?[\"'](?<version>[0-9][0-9A-Za-z.+-]*)[\"']\\s*\\)?",
        RegexOptions.CultureInvariant)]
    private static partial Regex ModernPluginRegex();

    [GeneratedRegex(
        "(?ix)com\\.android\\.tools\\.build\\s*:\\s*gradle\\s*:\\s*(?<version>[0-9][0-9A-Za-z.+-]*)",
        RegexOptions.CultureInvariant)]
    private static partial Regex LegacyClasspathRegex();
}