namespace FlutterBuildDoctor.Domain.Compatibility;

public enum CompatibilitySeverity
{
    Ready = 0,
    Info = 1,
    Warning = 2,
    Blocker = 3
}

public enum CompatibilityArea
{
    JavaGradle = 0,
    GradleAgp,
    AgpCompileSdk,
    KotlinToolchain,
    FlutterDart,
    AndroidPackages
}

public sealed record CompatibilityFinding(
    CompatibilityArea Area,
    CompatibilitySeverity Severity,
    string Current,
    string Required,
    string Recommended,
    string Evidence,
    string Message)
{
    public bool IsBlocker => Severity == CompatibilitySeverity.Blocker;
}

public sealed record CompatibilityRequirements(
    VersionConstraint JavaForGradle,
    VersionConstraint GradleForAgp,
    VersionConstraint AgpForCompileSdk,
    VersionConstraint KotlinForToolchain,
    VersionConstraint DartForFlutter,
    int RequiredCompileSdk,
    IReadOnlyCollection<int> RequiredAndroidPlatforms,
    IReadOnlyCollection<string> RequiredBuildTools,
    string? RecommendedJava = null,
    string? RecommendedGradle = null,
    string? RecommendedAgp = null,
    string? RecommendedKotlin = null,
    string? RecommendedDart = null);

public sealed record CompatibilitySnapshot(
    SemanticVersion? Java,
    SemanticVersion? Gradle,
    SemanticVersion? Agp,
    SemanticVersion? Kotlin,
    SemanticVersion? Flutter,
    SemanticVersion? Dart,
    int? CompileSdk,
    IReadOnlyCollection<int> InstalledAndroidPlatforms,
    IReadOnlyCollection<string> InstalledBuildTools);

public sealed record CompatibilityMatrix(
    IReadOnlyList<CompatibilityFinding> Findings,
    int Score,
    int BlockerCount,
    int WarningCount,
    int ReadyCount)
{
    public bool IsReady => BlockerCount == 0;
}

public interface ICompatibilityEngine
{
    CompatibilityMatrix Evaluate(CompatibilitySnapshot current, CompatibilityRequirements required);
}

public sealed class CompatibilityEngine : ICompatibilityEngine
{
    public CompatibilityMatrix Evaluate(CompatibilitySnapshot current, CompatibilityRequirements required)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(required);

        var findings = new List<CompatibilityFinding>
        {
            EvaluateVersion(
                CompatibilityArea.JavaGradle,
                "Java ↔ Gradle",
                current.Java,
                required.JavaForGradle,
                required.RecommendedJava,
                "The effective Java runtime must satisfy the Gradle compatibility requirement."),
            EvaluateVersion(
                CompatibilityArea.GradleAgp,
                "Gradle ↔ Android Gradle Plugin",
                current.Gradle,
                required.GradleForAgp,
                required.RecommendedGradle,
                "The project's Gradle wrapper must satisfy the selected Android Gradle Plugin requirement."),
            EvaluateVersion(
                CompatibilityArea.AgpCompileSdk,
                "Android Gradle Plugin ↔ compileSdk",
                current.Agp,
                required.AgpForCompileSdk,
                required.RecommendedAgp,
                $"AGP must support compileSdk {required.RequiredCompileSdk}."),
            EvaluateVersion(
                CompatibilityArea.KotlinToolchain,
                "Kotlin ↔ Gradle/AGP",
                current.Kotlin,
                required.KotlinForToolchain,
                required.RecommendedKotlin,
                "The Kotlin plugin must satisfy the toolchain compatibility requirement."),
            EvaluateVersion(
                CompatibilityArea.FlutterDart,
                "Flutter ↔ Dart project constraint",
                current.Dart,
                required.DartForFlutter,
                required.RecommendedDart,
                current.Flutter is null
                    ? "Flutter version was not detected; Dart project compatibility is evaluated from the effective Dart SDK only."
                    : $"Flutter {current.Flutter} provides Dart {current.Dart?.ToString() ?? "unknown"}; the project SDK constraint must accept it."),
            EvaluateCompileSdk(current.CompileSdk, required.RequiredCompileSdk),
            EvaluateAndroidPackages(current, required)
        };

        var blockerCount = findings.Count(x => x.Severity == CompatibilitySeverity.Blocker);
        var warningCount = findings.Count(x => x.Severity == CompatibilitySeverity.Warning);
        var readyCount = findings.Count(x => x.Severity == CompatibilitySeverity.Ready);
        var score = CalculateScore(findings);
        return new CompatibilityMatrix(findings, score, blockerCount, warningCount, readyCount);
    }

    private static CompatibilityFinding EvaluateVersion(
        CompatibilityArea area,
        string label,
        SemanticVersion? current,
        VersionConstraint requirement,
        string? recommended,
        string evidence)
    {
        if (current is null)
        {
            return new CompatibilityFinding(
                area,
                CompatibilitySeverity.Blocker,
                "Not detected",
                requirement.ToString(),
                recommended ?? requirement.ToString(),
                evidence,
                $"{label}: required component/version was not detected.");
        }

        var satisfied = requirement.IsSatisfiedBy(current.Value);
        return new CompatibilityFinding(
            area,
            satisfied ? CompatibilitySeverity.Ready : CompatibilitySeverity.Blocker,
            current.Value.ToString(),
            requirement.ToString(),
            recommended ?? requirement.ToString(),
            evidence,
            satisfied
                ? $"{label}: compatible."
                : $"{label}: {current} does not satisfy {requirement}.");
    }

    private static CompatibilityFinding EvaluateCompileSdk(int? current, int required)
    {
        if (current is null)
        {
            return new CompatibilityFinding(
                CompatibilityArea.AgpCompileSdk,
                CompatibilitySeverity.Blocker,
                "Not detected",
                required.ToString(),
                required.ToString(),
                "compileSdk could not be resolved from the Android project configuration.",
                "compileSdk is required for Android compatibility evaluation.");
        }

        var satisfied = current.Value >= required;
        return new CompatibilityFinding(
            CompatibilityArea.AgpCompileSdk,
            satisfied ? CompatibilitySeverity.Ready : CompatibilitySeverity.Blocker,
            current.Value.ToString(),
            $">={required}",
            required.ToString(),
            "compileSdk is compared against the required Android API level.",
            satisfied ? "compileSdk requirement satisfied." : $"compileSdk {current} is below required API {required}.");
    }

    private static CompatibilityFinding EvaluateAndroidPackages(CompatibilitySnapshot current, CompatibilityRequirements required)
    {
        var missingPlatforms = required.RequiredAndroidPlatforms
            .Where(api => !current.InstalledAndroidPlatforms.Contains(api))
            .OrderBy(api => api)
            .ToArray();
        var installedTools = new HashSet<string>(current.InstalledBuildTools, StringComparer.OrdinalIgnoreCase);
        var missingBuildTools = required.RequiredBuildTools
            .Where(version => !installedTools.Contains(version))
            .OrderBy(version => version, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var ready = missingPlatforms.Length == 0 && missingBuildTools.Length == 0;
        var requiredText = $"platforms=[{string.Join(",", required.RequiredAndroidPlatforms.OrderBy(x => x))}], build-tools=[{string.Join(",", required.RequiredBuildTools)}]";
        var currentText = $"platforms=[{string.Join(",", current.InstalledAndroidPlatforms.OrderBy(x => x))}], build-tools=[{string.Join(",", current.InstalledBuildTools)}]";
        var evidence = ready
            ? "All required Android SDK packages are installed."
            : $"Missing platforms: {(missingPlatforms.Length == 0 ? "none" : string.Join(",", missingPlatforms))}; missing build-tools: {(missingBuildTools.Length == 0 ? "none" : string.Join(",", missingBuildTools))}.";

        return new CompatibilityFinding(
            CompatibilityArea.AndroidPackages,
            ready ? CompatibilitySeverity.Ready : CompatibilitySeverity.Blocker,
            currentText,
            requiredText,
            requiredText,
            evidence,
            ready ? "Android platform/build-tools availability satisfied." : "Required Android SDK packages are missing.");
    }

    private static int CalculateScore(IEnumerable<CompatibilityFinding> findings)
    {
        var items = findings.ToArray();
        if (items.Length == 0) return 100;
        var penalty = items.Sum(item => item.Severity switch
        {
            CompatibilitySeverity.Blocker => 25,
            CompatibilitySeverity.Warning => 10,
            CompatibilitySeverity.Info => 3,
            _ => 0
        });
        return Math.Clamp(100 - penalty, 0, 100);
    }
}
