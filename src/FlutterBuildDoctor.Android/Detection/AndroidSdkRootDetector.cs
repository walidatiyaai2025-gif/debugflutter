using FlutterBuildDoctor.Application.Environment;

namespace FlutterBuildDoctor.Android.Detection;

public sealed class AndroidSdkRootDetector : IAndroidSdkRootDetector
{
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    public AndroidSdkRootDetectionResult Detect(EnvironmentVariableSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var sourceValues = GetConfiguredSources(snapshot).ToArray();
        if (sourceValues.Length == 0)
        {
            return new AndroidSdkRootDetectionResult(
                AndroidSdkRootDetectionStatus.MissingEffectiveRoot,
                EffectiveCandidate: null,
                Candidates: Array.Empty<AndroidSdkRootCandidate>(),
                HasConflict: false,
                Message: "No Android SDK root is configured in the captured ANDROID_SDK_ROOT or ANDROID_HOME values.");
        }

        var effectiveSource = sourceValues.FirstOrDefault(static source => source.Scope == VariableScope.Process);
        var grouped = new List<CandidateBuilder>();

        foreach (var source in sourceValues)
        {
            var normalized = Normalize(source.RawValue, out var normalizationError);
            var key = normalized ?? source.RawValue.Trim();
            var existing = grouped.FirstOrDefault(candidate => PathComparer.Equals(candidate.Key, key));
            if (existing is null)
            {
                existing = new CandidateBuilder(key, normalized, normalizationError);
                grouped.Add(existing);
            }

            existing.Sources.Add(source);
        }

        var effectiveKey = effectiveSource is null
            ? null
            : Normalize(effectiveSource.RawValue, out _) ?? effectiveSource.RawValue.Trim();

        var candidates = grouped
            .Select(builder => BuildCandidate(
                builder,
                effectiveKey is not null && PathComparer.Equals(builder.Key, effectiveKey)))
            .ToArray();

        var effectiveCandidate = candidates.FirstOrDefault(static candidate => candidate.IsEffective);
        var hasConflict = candidates.Length > 1;

        if (effectiveCandidate is null)
        {
            return new AndroidSdkRootDetectionResult(
                AndroidSdkRootDetectionStatus.MissingEffectiveRoot,
                EffectiveCandidate: null,
                Candidates: candidates,
                HasConflict: hasConflict,
                Message: candidates.Length == 0
                    ? "No Android SDK root candidates were available."
                    : "Android SDK roots exist in persisted User/Machine configuration, but the current process has no effective ANDROID_SDK_ROOT or ANDROID_HOME value.");
        }

        if (!effectiveCandidate.IsValid)
        {
            var suffix = hasConflict
                ? " Additional configured SDK root candidates are preserved as evidence and were not promoted automatically."
                : string.Empty;

            return new AndroidSdkRootDetectionResult(
                AndroidSdkRootDetectionStatus.EffectiveRootInvalid,
                effectiveCandidate,
                candidates,
                hasConflict,
                $"The effective Android SDK root '{effectiveCandidate.NormalizedPath}' is invalid: {effectiveCandidate.ValidationMessage}{suffix}");
        }

        var conflictSuffix = hasConflict
            ? $" {candidates.Length - 1} additional configured SDK root candidate(s) differ from the effective root."
            : string.Empty;

        return new AndroidSdkRootDetectionResult(
            AndroidSdkRootDetectionStatus.Succeeded,
            effectiveCandidate,
            candidates,
            hasConflict,
            $"Android SDK root detected at '{effectiveCandidate.NormalizedPath}'.{conflictSuffix}");
    }

    private static IEnumerable<AndroidSdkRootSourceEvidence> GetConfiguredSources(EnvironmentVariableSnapshot snapshot)
    {
        foreach (var source in EnumerateVariable(snapshot.AndroidSdkRoot))
            yield return source;

        foreach (var source in EnumerateVariable(snapshot.AndroidHome))
            yield return source;
    }

    private static IEnumerable<AndroidSdkRootSourceEvidence> EnumerateVariable(VariableRecord variable)
    {
        foreach (var value in new[] { variable.Process, variable.User, variable.Machine })
        {
            if (value.Status != VariableReadStatus.Present || string.IsNullOrWhiteSpace(value.Value))
                continue;

            yield return new AndroidSdkRootSourceEvidence(variable.Name, value.Scope, value.Value);
        }
    }

    private static string? Normalize(string rawValue, out string? error)
    {
        error = null;
        var trimmed = rawValue.Trim().Trim('"');
        if (trimmed.Length == 0)
        {
            error = "The configured path is empty after trimming quotes and whitespace.";
            return null;
        }

        if (trimmed.Contains('%', StringComparison.Ordinal))
        {
            error = "The configured path contains an unresolved environment-variable reference.";
            return null;
        }

        if (trimmed.StartsWith("\\\\", StringComparison.Ordinal))
        {
            error = "UNC/network SDK roots are not probed automatically.";
            return null;
        }

        try
        {
            if (!Path.IsPathFullyQualified(trimmed))
            {
                error = "The configured path is not fully qualified.";
                return null;
            }

            var fullPath = Path.GetFullPath(trimmed);
            var root = Path.GetPathRoot(fullPath);
            if (!string.IsNullOrEmpty(root) &&
                fullPath.Length > root.Length)
            {
                fullPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }

            return fullPath;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"The configured path could not be normalized: {ex.Message}";
            return null;
        }
    }

    private static AndroidSdkRootCandidate BuildCandidate(CandidateBuilder builder, bool isEffective)
    {
        if (builder.NormalizedPath is null)
        {
            return new AndroidSdkRootCandidate(
                builder.Key,
                builder.Sources.ToArray(),
                isEffective,
                Exists: false,
                HasRecognizedSdkLayout: false,
                HasPlatformToolsDirectory: false,
                HasPlatformsDirectory: false,
                HasBuildToolsDirectory: false,
                HasCmdlineToolsDirectory: false,
                HasLicensesDirectory: false,
                ValidationMessage: builder.NormalizationError ?? "The configured path is invalid.");
        }

        try
        {
            var exists = Directory.Exists(builder.NormalizedPath);
            if (!exists)
            {
                return new AndroidSdkRootCandidate(
                    builder.NormalizedPath,
                    builder.Sources.ToArray(),
                    isEffective,
                    Exists: false,
                    HasRecognizedSdkLayout: false,
                    HasPlatformToolsDirectory: false,
                    HasPlatformsDirectory: false,
                    HasBuildToolsDirectory: false,
                    HasCmdlineToolsDirectory: false,
                    HasLicensesDirectory: false,
                    ValidationMessage: "The configured directory does not exist.");
            }

            var platformTools = Directory.Exists(Path.Combine(builder.NormalizedPath, "platform-tools"));
            var platforms = Directory.Exists(Path.Combine(builder.NormalizedPath, "platforms"));
            var buildTools = Directory.Exists(Path.Combine(builder.NormalizedPath, "build-tools"));
            var cmdlineTools = Directory.Exists(Path.Combine(builder.NormalizedPath, "cmdline-tools"));
            var legacyTools = Directory.Exists(Path.Combine(builder.NormalizedPath, "tools"));
            var licenses = Directory.Exists(Path.Combine(builder.NormalizedPath, "licenses"));
            var emulator = Directory.Exists(Path.Combine(builder.NormalizedPath, "emulator"));
            var recognized = platformTools || platforms || buildTools || cmdlineTools || legacyTools || licenses || emulator;

            return new AndroidSdkRootCandidate(
                builder.NormalizedPath,
                builder.Sources.ToArray(),
                isEffective,
                Exists: true,
                HasRecognizedSdkLayout: recognized,
                HasPlatformToolsDirectory: platformTools,
                HasPlatformsDirectory: platforms,
                HasBuildToolsDirectory: buildTools,
                HasCmdlineToolsDirectory: cmdlineTools,
                HasLicensesDirectory: licenses,
                ValidationMessage: recognized
                    ? null
                    : "The directory exists but does not contain a recognized Android SDK layout marker.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new AndroidSdkRootCandidate(
                builder.NormalizedPath,
                builder.Sources.ToArray(),
                isEffective,
                Exists: false,
                HasRecognizedSdkLayout: false,
                HasPlatformToolsDirectory: false,
                HasPlatformsDirectory: false,
                HasBuildToolsDirectory: false,
                HasCmdlineToolsDirectory: false,
                HasLicensesDirectory: false,
                ValidationMessage: $"The configured SDK root could not be inspected: {ex.Message}");
        }
    }

    private sealed class CandidateBuilder
    {
        public CandidateBuilder(string key, string? normalizedPath, string? normalizationError)
        {
            Key = key;
            NormalizedPath = normalizedPath;
            NormalizationError = normalizationError;
        }

        public string Key { get; }
        public string? NormalizedPath { get; }
        public string? NormalizationError { get; }
        public List<AndroidSdkRootSourceEvidence> Sources { get; } = new();
    }
}
