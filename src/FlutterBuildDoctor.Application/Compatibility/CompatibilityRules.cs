namespace FlutterBuildDoctor.Application.Compatibility;

public static class CompatibilitySources
{
    public static readonly CompatibilitySource GradleJava = new(
        "Gradle Java Compatibility Matrix",
        "https://docs.gradle.org/current/userguide/compatibility.html",
        new DateOnly(2026, 8, 1));

    public static readonly CompatibilitySource AndroidGradlePlugin = new(
        "Android Gradle Plugin release/compatibility documentation",
        "https://developer.android.com/build/releases/",
        new DateOnly(2026, 7, 16));

    public static readonly CompatibilitySource AndroidApi = new(
        "Android API level minimum tool versions",
        "https://developer.android.com/build/releases/about-agp",
        new DateOnly(2026, 7, 16));

    public static readonly CompatibilitySource KotlinGradle = new(
        "Kotlin Gradle plugin compatibility",
        "https://kotlinlang.org/docs/gradle-configure-project.html",
        new DateOnly(2026, 7, 14));
}

public sealed class JavaGradleCompatibilityRule : ICompatibilityRule
{
    private static readonly JavaRuntimeRange[] Ranges =
    {
        new(8, "2.0", "8.15"),
        new(9, "4.3", "8.15"),
        new(10, "4.7", "8.15"),
        new(11, "5.0", "8.15"),
        new(12, "5.4", "8.15"),
        new(13, "6.0", "8.15"),
        new(14, "6.3", "8.15"),
        new(15, "6.7", "8.15"),
        new(16, "7.0", "8.15"),
        new(17, "7.3", null),
        new(18, "7.5", null),
        new(19, "7.6", null),
        new(20, "8.3", null),
        new(21, "8.5", null),
        new(22, "8.8", null),
        new(23, "8.10", null),
        new(24, "8.14", null),
        new(25, "9.1.0", null),
        new(26, "9.4.0", null)
    };

    public string RuleId => "JAVA_GRADLE";

    public IReadOnlyList<CompatibilityFinding> Evaluate(CompatibilityContext context)
    {
        if (!SemanticVersion.TryParse(context.JavaVersion, out var java) ||
            !SemanticVersion.TryParse(context.GradleVersion, out var gradle))
        {
            return Array.Empty<CompatibilityFinding>();
        }

        var range = Ranges.FirstOrDefault(item => item.JavaMajor == java.Major);
        if (range is null)
        {
            return new[]
            {
                Finding(
                    CompatibilitySeverity.Blocker,
                    $"Java {java.Major} is outside the captured Gradle runtime compatibility matrix (Java 8-26).",
                    context,
                    "Java 8-26 according to the current matrix",
                    "Choose a supported JDK or refresh the compatibility data.")
            };
        }

        var minimum = SemanticVersion.Parse(range.MinimumGradle);
        var maximumExclusive = range.MaximumGradleExclusive is null
            ? (SemanticVersion?)null
            : SemanticVersion.Parse(range.MaximumGradleExclusive);
        if (gradle < minimum || (maximumExclusive is { } maximum && gradle >= maximum))
        {
            var requirement = maximumExclusive is null
                ? $">={minimum}"
                : $">={minimum} <{maximumExclusive.Value}";
            return new[]
            {
                Finding(
                    CompatibilitySeverity.Blocker,
                    $"Gradle {gradle} cannot run on Java {java.Major} according to the captured Gradle matrix.",
                    context,
                    requirement,
                    $"Use a Gradle version in {requirement} or select a compatible JDK.")
            };
        }

        return Array.Empty<CompatibilityFinding>();
    }

    private CompatibilityFinding Finding(
        CompatibilitySeverity severity,
        string message,
        CompatibilityContext context,
        string required,
        string recommended)
        => new(
            RuleId,
            severity,
            "Java ↔ Gradle",
            message,
            $"Java {context.JavaVersion} / Gradle {context.GradleVersion}",
            required,
            recommended,
            CompatibilitySources.GradleJava);

    private sealed record JavaRuntimeRange(int JavaMajor, string MinimumGradle, string? MaximumGradleExclusive);
}

public sealed class GradleAgpCompatibilityRule : ICompatibilityRule
{
    private static readonly AgpGradleLine[] Lines =
    {
        new(7, 0, "7.0.2"),
        new(8, 0, "8.0"),
        new(8, 3, "8.4"),
        new(8, 5, "8.7"),
        new(8, 7, "8.9"),
        new(8, 9, "8.11.1"),
        new(8, 10, "8.11.1"),
        new(8, 11, "8.13"),
        new(8, 13, "8.13"),
        new(9, 0, "9.1.0"),
        new(9, 2, "9.4.1"),
        new(9, 3, "9.5.0"),
        new(9, 4, "9.6.0")
    };

    public string RuleId => "GRADLE_AGP";

    public IReadOnlyList<CompatibilityFinding> Evaluate(CompatibilityContext context)
    {
        if (!SemanticVersion.TryParse(context.AndroidGradlePluginVersion, out var agp) ||
            !SemanticVersion.TryParse(context.GradleVersion, out var gradle))
        {
            return Array.Empty<CompatibilityFinding>();
        }

        var line = Lines.FirstOrDefault(item => item.AgpMajor == agp.Major && item.AgpMinor == agp.Minor);
        if (line is null)
        {
            return new[]
            {
                new CompatibilityFinding(
                    RuleId,
                    CompatibilitySeverity.Warning,
                    "Gradle ↔ AGP",
                    $"AGP {agp} is not in the captured minimum-Gradle table; compatibility is not assumed.",
                    $"AGP {context.AndroidGradlePluginVersion} / Gradle {context.GradleVersion}",
                    Recommended: "Refresh compatibility data or verify against the matching AGP release notes.",
                    Source: CompatibilitySources.AndroidGradlePlugin)
            };
        }

        var minimum = SemanticVersion.Parse(line.MinimumGradle);
        if (gradle >= minimum)
        {
            return Array.Empty<CompatibilityFinding>();
        }

        return new[]
        {
            new CompatibilityFinding(
                RuleId,
                CompatibilitySeverity.Blocker,
                "Gradle ↔ AGP",
                $"AGP {agp.Major}.{agp.Minor} requires Gradle {minimum} or newer according to its release compatibility data.",
                $"AGP {context.AndroidGradlePluginVersion} / Gradle {context.GradleVersion}",
                $">={minimum}",
                $"Upgrade the Gradle wrapper to at least {minimum} or use a compatible AGP line.",
                CompatibilitySources.AndroidGradlePlugin)
        };
    }

    private sealed record AgpGradleLine(int AgpMajor, int AgpMinor, string MinimumGradle);
}

public sealed class AgpCompileSdkCompatibilityRule : ICompatibilityRule
{
    private static readonly IReadOnlyDictionary<int, string> MinimumAgpByApi = new Dictionary<int, string>
    {
        [33] = "7.2",
        [34] = "8.1.1",
        [35] = "8.6.0",
        [36] = "8.9.1",
        [37] = "9.1.1"
    };

    public string RuleId => "AGP_COMPILE_SDK";

    public IReadOnlyList<CompatibilityFinding> Evaluate(CompatibilityContext context)
    {
        if (context.CompileSdk is null ||
            !SemanticVersion.TryParse(context.AndroidGradlePluginVersion, out var agp))
        {
            return Array.Empty<CompatibilityFinding>();
        }

        if (!MinimumAgpByApi.TryGetValue(context.CompileSdk.Value, out var minimumText))
        {
            if (context.CompileSdk.Value < 33)
            {
                return Array.Empty<CompatibilityFinding>();
            }

            return new[]
            {
                new CompatibilityFinding(
                    RuleId,
                    CompatibilitySeverity.Warning,
                    "AGP ↔ compileSdk",
                    $"compileSdk {context.CompileSdk} is newer than the captured API compatibility table.",
                    $"AGP {context.AndroidGradlePluginVersion} / compileSdk {context.CompileSdk}",
                    Recommended: "Refresh Android API compatibility data before declaring this combination ready.",
                    Source: CompatibilitySources.AndroidApi)
            };
        }

        var minimum = SemanticVersion.Parse(minimumText);
        if (agp >= minimum)
        {
            return Array.Empty<CompatibilityFinding>();
        }

        return new[]
        {
            new CompatibilityFinding(
                RuleId,
                CompatibilitySeverity.Blocker,
                "AGP ↔ compileSdk",
                $"compileSdk {context.CompileSdk} requires AGP {minimum} or newer according to Android's minimum tool-version table.",
                $"AGP {context.AndroidGradlePluginVersion} / compileSdk {context.CompileSdk}",
                $">={minimum}",
                $"Upgrade AGP to at least {minimum} or lower compileSdk only if the project requirements allow it.",
                CompatibilitySources.AndroidApi)
        };
    }
}

public sealed class KotlinGradleAgpCompatibilityRule : ICompatibilityRule
{
    private static readonly KotlinBand[] Bands =
    {
        new("2.4.0", "2.4.10", "7.6.3", "9.5.0", "8.5.2", "9.1.0"),
        new("2.3.20", "2.3.21", "7.6.3", "9.3.0", "8.2.2", "9.0.0"),
        new("2.3.10", "2.3.10", "7.6.3", "9.0.0", "8.2.2", "9.0.0"),
        new("2.3.0", "2.3.0", "7.6.3", "9.0.0", "8.2.2", "8.13.0"),
        new("2.2.20", "2.2.21", "7.6.3", "8.14.0", "7.3.1", "8.11.1"),
        new("2.2.0", "2.2.10", "7.6.3", "8.14.0", "7.3.1", "8.10.0"),
        new("2.1.20", "2.1.21", "7.6.3", "8.12.1", "7.3.1", "8.7.2"),
        new("2.1.0", "2.1.10", "7.6.3", "8.10.0", "7.3.1", "8.7.2"),
        new("2.0.20", "2.0.21", "6.8.3", "8.8.0", "7.1.3", "8.5.0"),
        new("2.0.0", "2.0.0", "6.8.3", "8.5.0", "7.1.3", "8.3.1"),
        new("1.9.20", "1.9.25", "6.8.3", "8.1.1", "4.2.2", "8.1.0")
    };

    public string RuleId => "KOTLIN_GRADLE_AGP";

    public IReadOnlyList<CompatibilityFinding> Evaluate(CompatibilityContext context)
    {
        if (!SemanticVersion.TryParse(context.KotlinVersion, out var kotlin))
        {
            return Array.Empty<CompatibilityFinding>();
        }

        var band = Bands.FirstOrDefault(item => item.ContainsKotlin(kotlin));
        if (band is null)
        {
            return new[]
            {
                new CompatibilityFinding(
                    RuleId,
                    CompatibilitySeverity.Warning,
                    "Kotlin ↔ Gradle/AGP",
                    $"Kotlin {kotlin} is outside the captured Kotlin Gradle plugin compatibility bands.",
                    context.KotlinVersion,
                    Recommended: "Refresh Kotlin compatibility data before making an automated recommendation.",
                    Source: CompatibilitySources.KotlinGradle)
            };
        }

        var findings = new List<CompatibilityFinding>();
        if (SemanticVersion.TryParse(context.GradleVersion, out var gradle) && !band.ContainsGradle(gradle))
        {
            findings.Add(new CompatibilityFinding(
                RuleId,
                CompatibilitySeverity.Blocker,
                "Kotlin ↔ Gradle",
                $"Kotlin {kotlin} is outside its fully-supported Gradle range {band.MinimumGradle}-{band.MaximumGradle}.",
                $"Kotlin {context.KotlinVersion} / Gradle {context.GradleVersion}",
                $"{band.MinimumGradle}-{band.MaximumGradle}",
                "Select a Gradle version inside the supported range or use a compatible Kotlin plugin.",
                CompatibilitySources.KotlinGradle));
        }

        if (SemanticVersion.TryParse(context.AndroidGradlePluginVersion, out var agp) && !band.ContainsAgp(agp))
        {
            findings.Add(new CompatibilityFinding(
                RuleId,
                CompatibilitySeverity.Blocker,
                "Kotlin ↔ AGP",
                $"Kotlin {kotlin} is outside its fully-supported AGP range {band.MinimumAgp}-{band.MaximumAgp}.",
                $"Kotlin {context.KotlinVersion} / AGP {context.AndroidGradlePluginVersion}",
                $"{band.MinimumAgp}-{band.MaximumAgp}",
                "Select an AGP version inside the supported range or use a compatible Kotlin plugin.",
                CompatibilitySources.KotlinGradle));
        }

        return findings;
    }

    private sealed record KotlinBand(
        string MinimumKotlin,
        string MaximumKotlin,
        string MinimumGradle,
        string MaximumGradle,
        string MinimumAgp,
        string MaximumAgp)
    {
        public bool ContainsKotlin(SemanticVersion value) => Contains(value, MinimumKotlin, MaximumKotlin);
        public bool ContainsGradle(SemanticVersion value) => Contains(value, MinimumGradle, MaximumGradle);
        public bool ContainsAgp(SemanticVersion value) => Contains(value, MinimumAgp, MaximumAgp);

        private static bool Contains(SemanticVersion value, string minimum, string maximum)
            => value >= SemanticVersion.Parse(minimum) && value <= SemanticVersion.Parse(maximum);
    }
}

public sealed class DartConstraintCompatibilityRule : ICompatibilityRule
{
    public string RuleId => "DART_SDK_CONSTRAINT";

    public IReadOnlyList<CompatibilityFinding> Evaluate(CompatibilityContext context)
    {
        if (!SemanticVersion.TryParse(context.DartVersion, out var dart) ||
            string.IsNullOrWhiteSpace(context.DartSdkConstraint))
        {
            return Array.Empty<CompatibilityFinding>();
        }

        if (!VersionConstraint.TryParse(context.DartSdkConstraint, out var constraint))
        {
            return new[]
            {
                new CompatibilityFinding(
                    RuleId,
                    CompatibilitySeverity.Warning,
                    "Flutter ↔ Dart",
                    $"The project Dart SDK constraint '{context.DartSdkConstraint}' could not be evaluated safely.",
                    context.DartVersion,
                    context.DartSdkConstraint,
                    "Review the pubspec SDK constraint before changing Flutter/Dart versions.")
            };
        }

        if (constraint!.Contains(dart))
        {
            return Array.Empty<CompatibilityFinding>();
        }

        return new[]
        {
            new CompatibilityFinding(
                RuleId,
                CompatibilitySeverity.Blocker,
                "Flutter ↔ Dart",
                $"Dart {dart} does not satisfy the project's SDK constraint '{context.DartSdkConstraint}'.",
                context.DartVersion,
                context.DartSdkConstraint,
                "Use a Flutter SDK that bundles a Dart version satisfying the project constraint.")
        };
    }
}

public sealed class AndroidPackageAvailabilityRule : ICompatibilityRule
{
    public string RuleId => "ANDROID_PACKAGES";

    public IReadOnlyList<CompatibilityFinding> Evaluate(CompatibilityContext context)
    {
        var findings = new List<CompatibilityFinding>();
        if (context.CompileSdk is { } compileSdk &&
            context.InstalledAndroidPlatforms is not null &&
            !context.InstalledAndroidPlatforms.Contains(compileSdk))
        {
            findings.Add(new CompatibilityFinding(
                RuleId,
                CompatibilitySeverity.Blocker,
                "Android SDK packages",
                $"Required Android platform android-{compileSdk} is not installed.",
                string.Join(", ", context.InstalledAndroidPlatforms.Order()),
                $"android-{compileSdk}",
                $"Install platform android-{compileSdk} and re-run detection."));
        }

        if (!string.IsNullOrWhiteSpace(context.RequiredBuildToolsVersion) &&
            SemanticVersion.TryParse(context.RequiredBuildToolsVersion, out var requiredBuildTools) &&
            context.InstalledBuildToolsVersions is not null)
        {
            var hasCompatible = context.InstalledBuildToolsVersions
                .Select(version => SemanticVersion.TryParse(version, out var parsed) ? parsed : (SemanticVersion?)null)
                .Any(version => version is { } parsed && parsed >= requiredBuildTools);
            if (!hasCompatible)
            {
                findings.Add(new CompatibilityFinding(
                    RuleId,
                    CompatibilitySeverity.Blocker,
                    "Android SDK packages",
                    $"Required Android build-tools {requiredBuildTools} or newer were not found.",
                    string.Join(", ", context.InstalledBuildToolsVersions),
                    $">={requiredBuildTools}",
                    $"Install build-tools {requiredBuildTools} or a compatible newer package and re-run detection."));
            }
        }

        return findings;
    }
}
