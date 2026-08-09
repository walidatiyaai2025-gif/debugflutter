using System.IO;

namespace FlutterBuildDoctor.Flutter.ProjectAnalysis;

public sealed class GradleDslDetector : IGradleDslDetector
{
    private static readonly (GradleScriptRole Role, string RelativePath, GradleDslKind Dsl)[] KnownScripts =
    {
        (GradleScriptRole.Settings, "settings.gradle", GradleDslKind.Groovy),
        (GradleScriptRole.Settings, "settings.gradle.kts", GradleDslKind.Kotlin),
        (GradleScriptRole.ProjectBuild, "build.gradle", GradleDslKind.Groovy),
        (GradleScriptRole.ProjectBuild, "build.gradle.kts", GradleDslKind.Kotlin),
        (GradleScriptRole.AppBuild, Path.Combine("app", "build.gradle"), GradleDslKind.Groovy),
        (GradleScriptRole.AppBuild, Path.Combine("app", "build.gradle.kts"), GradleDslKind.Kotlin)
    };

    public GradleDslDetectionResult Detect(FlutterProjectRootResult projectRoot)
    {
        ArgumentNullException.ThrowIfNull(projectRoot);

        if (!projectRoot.IsSuccess || string.IsNullOrWhiteSpace(projectRoot.EffectiveRoot))
        {
            return Result(
                GradleDslDetectionStatus.ProjectRootUnavailable,
                projectRoot,
                null,
                null,
                Array.Empty<GradleScriptEvidence>(),
                "A successfully resolved Flutter project root is required before Gradle DSL detection.");
        }

        string androidDirectory;
        try
        {
            androidDirectory = Path.GetFullPath(Path.Combine(projectRoot.EffectiveRoot, "android"));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Result(
                GradleDslDetectionStatus.InspectionFailed,
                projectRoot,
                null,
                null,
                Array.Empty<GradleScriptEvidence>(),
                $"Android project path is invalid: {ex.Message}");
        }

        if (!Directory.Exists(androidDirectory))
        {
            return Result(
                GradleDslDetectionStatus.AndroidDirectoryMissing,
                projectRoot,
                androidDirectory,
                null,
                Array.Empty<GradleScriptEvidence>(),
                "The Flutter project does not contain an Android project directory.");
        }

        try
        {
            if (IsReparsePoint(androidDirectory))
            {
                return Result(
                    GradleDslDetectionStatus.InspectionFailed,
                    projectRoot,
                    androidDirectory,
                    null,
                    Array.Empty<GradleScriptEvidence>(),
                    "The Android project directory is a reparse point or symbolic link and will not be traversed outside the resolved Flutter project boundary.");
            }

            var appDirectory = Path.Combine(androidDirectory, "app");
            if (Directory.Exists(appDirectory) && IsReparsePoint(appDirectory))
            {
                return Result(
                    GradleDslDetectionStatus.InspectionFailed,
                    projectRoot,
                    androidDirectory,
                    null,
                    Array.Empty<GradleScriptEvidence>(),
                    "The Android app directory is a reparse point or symbolic link and will not be traversed outside the resolved Flutter project boundary.");
            }

            var scripts = new List<GradleScriptEvidence>();
            var unsafeScripts = new List<string>();
            foreach (var known in KnownScripts)
            {
                var path = Path.GetFullPath(Path.Combine(androidDirectory, known.RelativePath));
                if (!File.Exists(path))
                    continue;

                if (IsReparsePoint(path))
                {
                    unsafeScripts.Add(path);
                    continue;
                }

                scripts.Add(new GradleScriptEvidence(known.Role, known.Dsl, path));
            }

            scripts.Sort((left, right) =>
            {
                var roleComparison = left.Role.CompareTo(right.Role);
                return roleComparison != 0
                    ? roleComparison
                    : StringComparer.OrdinalIgnoreCase.Compare(left.Path, right.Path);
            });

            if (unsafeScripts.Count > 0)
            {
                return Result(
                    GradleDslDetectionStatus.InspectionFailed,
                    projectRoot,
                    androidDirectory,
                    null,
                    scripts.ToArray(),
                    $"Gradle script discovery found {unsafeScripts.Count} reparse-point/symbolic-link script(s). Unsafe script paths were not selected for downstream parsing.");
            }

            var ambiguousRoles = scripts
                .GroupBy(script => script.Role)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();

            if (ambiguousRoles.Length > 0)
            {
                return Result(
                    GradleDslDetectionStatus.Ambiguous,
                    projectRoot,
                    androidDirectory,
                    GradleDslKind.Mixed,
                    scripts.ToArray(),
                    $"Both Groovy and Kotlin Gradle scripts exist for: {string.Join(", ", ambiguousRoles)}. No script was selected implicitly.");
            }

            var buildScripts = scripts
                .Where(script => script.Role is GradleScriptRole.ProjectBuild or GradleScriptRole.AppBuild)
                .ToArray();
            if (buildScripts.Length == 0)
            {
                return Result(
                    GradleDslDetectionStatus.BuildScriptsMissing,
                    projectRoot,
                    androidDirectory,
                    null,
                    scripts.ToArray(),
                    "No supported Android project/app build.gradle or build.gradle.kts script was found.");
            }

            var distinctDslKinds = scripts
                .Select(script => script.Dsl)
                .Distinct()
                .ToArray();
            var effectiveDsl = distinctDslKinds.Length == 1
                ? distinctDslKinds[0]
                : GradleDslKind.Mixed;

            var missingRoles = new List<string>();
            if (scripts.All(script => script.Role != GradleScriptRole.ProjectBuild))
                missingRoles.Add("project build script");
            if (scripts.All(script => script.Role != GradleScriptRole.AppBuild))
                missingRoles.Add("app build script");
            if (scripts.All(script => script.Role != GradleScriptRole.Settings))
                missingRoles.Add("settings script");

            var completeness = missingRoles.Count == 0
                ? "All standard Android Gradle script roles were detected."
                : $"Missing optional/expected evidence: {string.Join(", ", missingRoles)}.";

            return Result(
                GradleDslDetectionStatus.Succeeded,
                projectRoot,
                androidDirectory,
                effectiveDsl,
                scripts.ToArray(),
                $"Detected {effectiveDsl} Gradle DSL from {scripts.Count} script(s). {completeness}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Result(
                GradleDslDetectionStatus.InspectionFailed,
                projectRoot,
                androidDirectory,
                null,
                Array.Empty<GradleScriptEvidence>(),
                $"Gradle script inspection failed: {ex.Message}");
        }
    }

    private static bool IsReparsePoint(string path)
        => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static GradleDslDetectionResult Result(
        GradleDslDetectionStatus status,
        FlutterProjectRootResult projectRoot,
        string? androidDirectory,
        GradleDslKind? effectiveDsl,
        IReadOnlyList<GradleScriptEvidence> scripts,
        string message)
        => new(status, projectRoot, androidDirectory, effectiveDsl, scripts, message);
}
