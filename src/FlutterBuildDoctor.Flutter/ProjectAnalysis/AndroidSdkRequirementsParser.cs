using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Flutter.ProjectAnalysis;

public sealed partial class AndroidSdkRequirementsParser : IAndroidSdkRequirementsParser
{
    private const long MaxScriptBytes = 512 * 1024;

    public AndroidSdkRequirementsResult Parse(GradleDslDetectionResult gradleDsl)
    {
        ArgumentNullException.ThrowIfNull(gradleDsl);

        if (!gradleDsl.IsSuccess || string.IsNullOrWhiteSpace(gradleDsl.AndroidDirectory))
        {
            return Result(
                AndroidSdkRequirementsStatus.GradleDslUnavailable,
                gradleDsl,
                null,
                null,
                null,
                Array.Empty<AndroidSdkLevelEvidence>(),
                Array.Empty<AndroidSdkLevelField>(),
                "A successful FBD-604 Gradle DSL result is required before Android SDK level parsing.");
        }

        var appScripts = gradleDsl.Scripts
            .Where(script => script.Role == GradleScriptRole.AppBuild)
            .ToArray();
        if (appScripts.Length == 0)
        {
            return Result(
                AndroidSdkRequirementsStatus.AppBuildScriptUnavailable,
                gradleDsl,
                null,
                null,
                null,
                Array.Empty<AndroidSdkLevelEvidence>(),
                Array.Empty<AndroidSdkLevelField>(),
                "FBD-604 did not provide an Android app build script. SDK levels were not inferred from unrelated files.");
        }

        if (appScripts.Length != 1)
        {
            return Result(
                AndroidSdkRequirementsStatus.Ambiguous,
                gradleDsl,
                null,
                null,
                null,
                Array.Empty<AndroidSdkLevelEvidence>(),
                Array.Empty<AndroidSdkLevelField>(),
                "Multiple Android app build scripts were supplied; no SDK requirement source was selected implicitly.");
        }

        var appScript = appScripts[0];
        string androidDirectory;
        string appDirectory;
        string scriptPath;
        string expectedPath;
        try
        {
            androidDirectory = Path.GetFullPath(gradleDsl.AndroidDirectory);
            appDirectory = Path.GetFullPath(Path.Combine(androidDirectory, "app"));
            scriptPath = Path.GetFullPath(appScript.Path);
            expectedPath = Path.GetFullPath(Path.Combine(
                appDirectory,
                appScript.Dsl == GradleDslKind.Kotlin ? "build.gradle.kts" : "build.gradle"));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Result(
                AndroidSdkRequirementsStatus.UnsafePath,
                gradleDsl,
                null,
                null,
                null,
                Array.Empty<AndroidSdkLevelEvidence>(),
                Array.Empty<AndroidSdkLevelField>(),
                $"Android app build-script path is invalid: {ex.Message}");
        }

        if (!PathsEqual(scriptPath, expectedPath))
        {
            return Result(
                AndroidSdkRequirementsStatus.UnsafePath,
                gradleDsl,
                null,
                null,
                null,
                Array.Empty<AndroidSdkLevelEvidence>(),
                Array.Empty<AndroidSdkLevelField>(),
                "FBD-604 supplied an app build script outside the expected android/app Gradle location.");
        }

        string text;
        try
        {
            if (!Directory.Exists(androidDirectory) || IsReparsePoint(androidDirectory) ||
                !Directory.Exists(appDirectory) || IsReparsePoint(appDirectory))
            {
                return Result(
                    AndroidSdkRequirementsStatus.UnsafePath,
                    gradleDsl,
                    null,
                    null,
                    null,
                    Array.Empty<AndroidSdkLevelEvidence>(),
                    Array.Empty<AndroidSdkLevelField>(),
                    "The Android/app project boundary is missing or is now a reparse point/symbolic link.");
            }

            if (!File.Exists(scriptPath))
            {
                return Result(
                    AndroidSdkRequirementsStatus.AppBuildScriptUnavailable,
                    gradleDsl,
                    null,
                    null,
                    null,
                    Array.Empty<AndroidSdkLevelEvidence>(),
                    Array.Empty<AndroidSdkLevelField>(),
                    "The FBD-604 app build-script evidence is stale because the file is no longer available.");
            }

            if (IsReparsePoint(scriptPath))
            {
                return Result(
                    AndroidSdkRequirementsStatus.UnsafePath,
                    gradleDsl,
                    null,
                    null,
                    null,
                    Array.Empty<AndroidSdkLevelEvidence>(),
                    Array.Empty<AndroidSdkLevelField>(),
                    "The Android app build script is a reparse point/symbolic link and was not followed.");
            }

            if (new FileInfo(scriptPath).Length > MaxScriptBytes)
            {
                return Result(
                    AndroidSdkRequirementsStatus.FileTooLarge,
                    gradleDsl,
                    null,
                    null,
                    null,
                    Array.Empty<AndroidSdkLevelEvidence>(),
                    Array.Empty<AndroidSdkLevelField>(),
                    $"The Android app build script exceeds the {MaxScriptBytes} byte inspection limit.");
            }

            text = File.ReadAllText(scriptPath, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Result(
                AndroidSdkRequirementsStatus.ReadFailed,
                gradleDsl,
                null,
                null,
                null,
                Array.Empty<AndroidSdkLevelEvidence>(),
                Array.Empty<AndroidSdkLevelField>(),
                $"The Android app build script could not be read: {ex.Message}");
        }

        var code = RemoveCommentsAndStrings(text);
        var evidence = new List<AndroidSdkLevelEvidence>();

        AddEvidence(
            AndroidSdkLevelField.CompileSdk,
            CompileStaticRegex(),
            CompileFlutterRegex(),
            "flutter.compileSdkVersion",
            code,
            scriptPath,
            evidence);
        AddEvidence(
            AndroidSdkLevelField.MinSdk,
            MinStaticRegex(),
            MinFlutterRegex(),
            "flutter.minSdkVersion",
            code,
            scriptPath,
            evidence);
        AddEvidence(
            AndroidSdkLevelField.TargetSdk,
            TargetStaticRegex(),
            TargetFlutterRegex(),
            "flutter.targetSdkVersion",
            code,
            scriptPath,
            evidence);

        if (TrySelect(AndroidSdkLevelField.CompileSdk, evidence, scriptPath, out var compileSdk, out var compileAmbiguous) is false ||
            TrySelect(AndroidSdkLevelField.MinSdk, evidence, scriptPath, out var minSdk, out var minAmbiguous) is false ||
            TrySelect(AndroidSdkLevelField.TargetSdk, evidence, scriptPath, out var targetSdk, out var targetAmbiguous) is false)
        {
            var ambiguousFields = new List<string>();
            if (compileAmbiguous) ambiguousFields.Add("compileSdk");
            if (minAmbiguous) ambiguousFields.Add("minSdk");
            if (targetAmbiguous) ambiguousFields.Add("targetSdk");

            return Result(
                AndroidSdkRequirementsStatus.Ambiguous,
                gradleDsl,
                compileSdk,
                minSdk,
                targetSdk,
                evidence.ToArray(),
                Array.Empty<AndroidSdkLevelField>(),
                $"Conflicting Android SDK requirement declarations were found for {string.Join(", ", ambiguousFields)}; no value was selected implicitly.");
        }

        var unresolved = new List<AndroidSdkLevelField>();
        if (compileSdk is null && CompileMarkerRegex().IsMatch(code)) unresolved.Add(AndroidSdkLevelField.CompileSdk);
        if (minSdk is null && MinMarkerRegex().IsMatch(code)) unresolved.Add(AndroidSdkLevelField.MinSdk);
        if (targetSdk is null && TargetMarkerRegex().IsMatch(code)) unresolved.Add(AndroidSdkLevelField.TargetSdk);

        var resolvedCount = new[] { compileSdk, minSdk, targetSdk }.Count(value => value is not null);
        if (resolvedCount == 0)
        {
            return Result(
                AndroidSdkRequirementsStatus.RequirementsNotFound,
                gradleDsl,
                null,
                null,
                null,
                evidence.ToArray(),
                unresolved.ToArray(),
                unresolved.Count > 0
                    ? "Android SDK requirement fields were present, but only unsupported dynamic expressions were found. Values were not guessed."
                    : "No supported compileSdk/minSdk/targetSdk declarations were found in the Android app build script.");
        }

        var status = resolvedCount == 3
            ? AndroidSdkRequirementsStatus.Succeeded
            : AndroidSdkRequirementsStatus.Partial;
        var missingNames = new List<string>();
        if (compileSdk is null) missingNames.Add("compileSdk");
        if (minSdk is null) missingNames.Add("minSdk");
        if (targetSdk is null) missingNames.Add("targetSdk");

        return Result(
            status,
            gradleDsl,
            compileSdk,
            minSdk,
            targetSdk,
            evidence.ToArray(),
            unresolved.ToArray(),
            status == AndroidSdkRequirementsStatus.Succeeded
                ? "compileSdk, minSdk, and targetSdk requirements were detected without executing Gradle."
                : $"Android SDK requirements were partially detected; unresolved/missing fields: {string.Join(", ", missingNames)}.");
    }

    private static void AddEvidence(
        AndroidSdkLevelField field,
        Regex staticRegex,
        Regex flutterRegex,
        string canonicalFlutterReference,
        string code,
        string scriptPath,
        ICollection<AndroidSdkLevelEvidence> evidence)
    {
        foreach (Match match in staticRegex.Matches(code))
        {
            if (int.TryParse(match.Groups["api"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var apiLevel))
            {
                evidence.Add(new AndroidSdkLevelEvidence(
                    field,
                    AndroidSdkLevelValueKind.StaticApiLevel,
                    apiLevel,
                    null,
                    scriptPath));
            }
        }

        foreach (Match _ in flutterRegex.Matches(code))
        {
            evidence.Add(new AndroidSdkLevelEvidence(
                field,
                AndroidSdkLevelValueKind.FlutterReference,
                null,
                canonicalFlutterReference,
                scriptPath));
        }
    }

    private static bool TrySelect(
        AndroidSdkLevelField field,
        IReadOnlyList<AndroidSdkLevelEvidence> evidence,
        string scriptPath,
        out AndroidSdkLevelValue? selected,
        out bool ambiguous)
    {
        var fieldEvidence = evidence.Where(item => item.Field == field).ToArray();
        var distinct = fieldEvidence
            .Select(item => item.Kind == AndroidSdkLevelValueKind.StaticApiLevel
                ? "static:" + item.ApiLevel?.ToString(CultureInfo.InvariantCulture)
                : "flutter:" + item.FlutterReference)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ambiguous = distinct.Length > 1;
        if (ambiguous || fieldEvidence.Length == 0)
        {
            selected = null;
            return !ambiguous;
        }

        var first = fieldEvidence[0];
        selected = new AndroidSdkLevelValue(
            field,
            first.Kind,
            first.ApiLevel,
            first.FlutterReference,
            scriptPath);
        return true;
    }

    private static string RemoveCommentsAndStrings(string text)
    {
        var output = new StringBuilder(text.Length);
        var inBlockComment = false;
        var inLineComment = false;
        char quote = '\0';
        var escaped = false;

        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            var next = index + 1 < text.Length ? text[index + 1] : '\0';

            if (inLineComment)
            {
                if (current is '\r' or '\n')
                {
                    inLineComment = false;
                    output.Append(current);
                }
                else
                {
                    output.Append(' ');
                }
                continue;
            }

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    output.Append("  ");
                    index++;
                    inBlockComment = false;
                }
                else
                {
                    output.Append(current is '\r' or '\n' ? current : ' ');
                }
                continue;
            }

            if (quote != '\0')
            {
                output.Append(current is '\r' or '\n' ? current : ' ');
                if (escaped)
                {
                    escaped = false;
                    continue;
                }
                if (current == '\\') escaped = true;
                else if (current == quote) quote = '\0';
                continue;
            }

            if (current == '/' && next == '/')
            {
                output.Append("  ");
                index++;
                inLineComment = true;
                continue;
            }

            if (current == '/' && next == '*')
            {
                output.Append("  ");
                index++;
                inBlockComment = true;
                continue;
            }

            if (current is '\'' or '"')
            {
                output.Append(' ');
                quote = current;
                continue;
            }

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

    private static AndroidSdkRequirementsResult Result(
        AndroidSdkRequirementsStatus status,
        GradleDslDetectionResult gradleDsl,
        AndroidSdkLevelValue? compileSdk,
        AndroidSdkLevelValue? minSdk,
        AndroidSdkLevelValue? targetSdk,
        IReadOnlyList<AndroidSdkLevelEvidence> evidence,
        IReadOnlyList<AndroidSdkLevelField> unresolvedFields,
        string message)
        => new(status, gradleDsl, compileSdk, minSdk, targetSdk, evidence, unresolvedFields, message);

    [GeneratedRegex(@"\bcompileSdk(?:Version)?\b\s*(?:=\s*)?(?<api>[0-9]+)\b", RegexOptions.CultureInvariant)]
    private static partial Regex CompileStaticRegex();

    [GeneratedRegex(@"\bcompileSdk(?:Version)?\b\s*(?:=\s*)?flutter\s*\.\s*compileSdkVersion\b", RegexOptions.CultureInvariant)]
    private static partial Regex CompileFlutterRegex();

    [GeneratedRegex(@"\bminSdk(?:Version)?\b\s*(?:=\s*)?(?<api>[0-9]+)\b", RegexOptions.CultureInvariant)]
    private static partial Regex MinStaticRegex();

    [GeneratedRegex(@"\bminSdk(?:Version)?\b\s*(?:=\s*)?flutter\s*\.\s*minSdkVersion\b", RegexOptions.CultureInvariant)]
    private static partial Regex MinFlutterRegex();

    [GeneratedRegex(@"\btargetSdk(?:Version)?\b\s*(?:=\s*)?(?<api>[0-9]+)\b", RegexOptions.CultureInvariant)]
    private static partial Regex TargetStaticRegex();

    [GeneratedRegex(@"\btargetSdk(?:Version)?\b\s*(?:=\s*)?flutter\s*\.\s*targetSdkVersion\b", RegexOptions.CultureInvariant)]
    private static partial Regex TargetFlutterRegex();

    [GeneratedRegex(@"\bcompileSdk(?:Version)?\b", RegexOptions.CultureInvariant)]
    private static partial Regex CompileMarkerRegex();

    [GeneratedRegex(@"\bminSdk(?:Version)?\b", RegexOptions.CultureInvariant)]
    private static partial Regex MinMarkerRegex();

    [GeneratedRegex(@"\btargetSdk(?:Version)?\b", RegexOptions.CultureInvariant)]
    private static partial Regex TargetMarkerRegex();
}
