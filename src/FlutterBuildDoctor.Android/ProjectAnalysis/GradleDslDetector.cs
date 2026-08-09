using System.IO;

namespace FlutterBuildDoctor.Android.ProjectAnalysis;

public sealed class GradleDslDetector : IGradleDslDetector
{
    public GradleDslDetectionResult Detect(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
            return Failure(GradleDslDetectionStatus.InvalidRequest, null, null, "A Flutter project root is required.");

        string normalizedProjectRoot;
        try
        {
            normalizedProjectRoot = Path.GetFullPath(projectRoot.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failure(
                GradleDslDetectionStatus.InvalidRequest,
                projectRoot,
                null,
                $"Project root path is invalid: {ex.Message}");
        }

        if (!Directory.Exists(normalizedProjectRoot))
        {
            return Failure(
                GradleDslDetectionStatus.ProjectRootNotFound,
                normalizedProjectRoot,
                null,
                "Flutter project root does not exist or is not accessible.");
        }

        var androidRoot = Path.Combine(normalizedProjectRoot, "android");
        if (!Directory.Exists(androidRoot))
        {
            return Failure(
                GradleDslDetectionStatus.AndroidDirectoryNotFound,
                normalizedProjectRoot,
                androidRoot,
                "The Flutter project does not contain an android directory.");
        }

        try
        {
            if (IsReparsePoint(androidRoot))
            {
                return Failure(
                    GradleDslDetectionStatus.InspectionFailed,
                    normalizedProjectRoot,
                    androidRoot,
                    "The android directory is a reparse point or symbolic link and will not be followed outside the resolved project evidence boundary.");
            }

            var scripts = new List<GradleScriptEvidence>();
            var conflicts = new List<string>();

            InspectRole(androidRoot, "settings.gradle", "settings.gradle.kts", GradleScriptRole.Settings, scripts, conflicts);
            InspectRole(androidRoot, "build.gradle", "build.gradle.kts", GradleScriptRole.ProjectBuild, scripts, conflicts);

            var appRoot = Path.Combine(androidRoot, "app");
            if (Directory.Exists(appRoot))
            {
                if (IsReparsePoint(appRoot))
                {
                    return new GradleDslDetectionResult(
                        GradleDslDetectionStatus.InspectionFailed,
                        normalizedProjectRoot,
                        androidRoot,
                        GradleDslKind.Unknown,
                        scripts.ToArray(),
                        "The android/app directory is a reparse point or symbolic link and will not be followed.");
                }

                InspectRole(appRoot, "build.gradle", "build.gradle.kts", GradleScriptRole.AppBuild, scripts, conflicts);
            }

            if (conflicts.Count > 0)
            {
                return new GradleDslDetectionResult(
                    GradleDslDetectionStatus.ConflictingScripts,
                    normalizedProjectRoot,
                    androidRoot,
                    EffectiveDsl(scripts),
                    scripts.ToArray(),
                    $"Both Groovy and Kotlin DSL files exist for {string.Join(", ", conflicts)}. Resolve the duplicate script layout before static Gradle analysis continues.");
            }

            var buildScripts = scripts
                .Where(script => script.Role is GradleScriptRole.ProjectBuild or GradleScriptRole.AppBuild)
                .ToArray();
            if (buildScripts.Length == 0)
            {
                return new GradleDslDetectionResult(
                    GradleDslDetectionStatus.BuildScriptsNotFound,
                    normalizedProjectRoot,
                    androidRoot,
                    GradleDslKind.Unknown,
                    scripts.ToArray(),
                    "No supported android/build.gradle(.kts) or android/app/build.gradle(.kts) build script was found.");
            }

            var effectiveDsl = EffectiveDsl(scripts);
            var status = effectiveDsl == GradleDslKind.Mixed
                ? GradleDslDetectionStatus.MixedDsl
                : GradleDslDetectionStatus.Succeeded;
            var message = status == GradleDslDetectionStatus.MixedDsl
                ? "Gradle scripts use a mixed Groovy/Kotlin DSL layout. Each script path and DSL was preserved for downstream parsers."
                : $"Detected {effectiveDsl} Gradle DSL from the Android project script layout.";

            return new GradleDslDetectionResult(
                status,
                normalizedProjectRoot,
                androidRoot,
                effectiveDsl,
                scripts.ToArray(),
                message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Failure(
                GradleDslDetectionStatus.InspectionFailed,
                normalizedProjectRoot,
                androidRoot,
                $"Gradle script inspection failed: {ex.Message}");
        }
    }

    private static void InspectRole(
        string directory,
        string groovyFileName,
        string kotlinFileName,
        GradleScriptRole role,
        ICollection<GradleScriptEvidence> scripts,
        ICollection<string> conflicts)
    {
        var groovyPath = Path.Combine(directory, groovyFileName);
        var kotlinPath = Path.Combine(directory, kotlinFileName);
        var hasGroovy = IsRegularFile(groovyPath);
        var hasKotlin = IsRegularFile(kotlinPath);

        if (hasGroovy)
            scripts.Add(new GradleScriptEvidence(role, GradleDslKind.Groovy, Path.GetFullPath(groovyPath)));
        if (hasKotlin)
            scripts.Add(new GradleScriptEvidence(role, GradleDslKind.Kotlin, Path.GetFullPath(kotlinPath)));

        if (hasGroovy && hasKotlin)
            conflicts.Add(role.ToString());
    }

    private static bool IsRegularFile(string path)
    {
        if (!File.Exists(path))
            return false;

        var attributes = File.GetAttributes(path);
        return (attributes & FileAttributes.ReparsePoint) == 0;
    }

    private static bool IsReparsePoint(string path)
        => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static GradleDslKind EffectiveDsl(IReadOnlyCollection<GradleScriptEvidence> scripts)
    {
        var kinds = scripts
            .Select(script => script.Dsl)
            .Where(kind => kind is GradleDslKind.Groovy or GradleDslKind.Kotlin)
            .Distinct()
            .ToArray();

        return kinds.Length switch
        {
            0 => GradleDslKind.Unknown,
            1 => kinds[0],
            _ => GradleDslKind.Mixed
        };
    }

    private static GradleDslDetectionResult Failure(
        GradleDslDetectionStatus status,
        string? projectRoot,
        string? androidRoot,
        string message)
        => new(
            status,
            projectRoot,
            androidRoot,
            GradleDslKind.Unknown,
            Array.Empty<GradleScriptEvidence>(),
            message);
}
