using System.Security.Cryptography;
using System.Text;

namespace FlutterBuildDoctor.Application.Builds;

public enum PlannedBuildMode
{
    Debug = 0,
    Profile = 1,
    Release = 2
}

public enum PlannedArtifactKind
{
    Apk = 0,
    Aab = 1
}

public sealed record BuildVariant(
    string? Flavor,
    string Target,
    PlannedBuildMode Mode,
    PlannedArtifactKind Artifact,
    int? RetryBudget = null);

public sealed record PlannedBuildVariant(
    string? Flavor,
    string Target,
    PlannedBuildMode Mode,
    PlannedArtifactKind Artifact,
    int RetryBudget);

public sealed record BuildMatrixPlan(IReadOnlyList<PlannedBuildVariant> Variants, string Fingerprint);

public static class BuildMatrixPlanner
{
    public const int MaxVariants = 32;
    public const int MaxRetries = 2;

    public static BuildMatrixPlan Create(IEnumerable<BuildVariant> variants)
    {
        ArgumentNullException.ThrowIfNull(variants);
        var materialized = variants.ToArray();
        if (materialized.Length == 0)
        {
            materialized = new[] { new BuildVariant(null, "lib/main.dart", PlannedBuildMode.Debug, PlannedArtifactKind.Apk) };
        }

        if (materialized.Length > MaxVariants)
        {
            throw new ArgumentOutOfRangeException(nameof(variants), $"Build matrix cannot exceed {MaxVariants} variants.");
        }

        var normalized = materialized.Select(Normalize).ToArray();
        var deduplicated = normalized
            .GroupBy(CanonicalKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(variant => variant.Mode)
            .ThenBy(variant => variant.Artifact)
            .ThenBy(variant => variant.Flavor ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(variant => variant.Target, StringComparer.Ordinal)
            .ToArray();

        return new BuildMatrixPlan(deduplicated, Fingerprint(deduplicated));
    }

    public static string? NormalizeFlavor(string? flavor)
    {
        if (string.IsNullOrWhiteSpace(flavor)) return null;
        var normalized = flavor.Trim().ToLowerInvariant().Replace('_', '-');
        if (normalized.Length > 64 || normalized.Any(character => char.IsControl(character) || char.IsWhiteSpace(character)))
        {
            throw new ArgumentException("Build flavor is invalid.", nameof(flavor));
        }

        return normalized;
    }

    public static string NormalizeTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(target) || target.Any(char.IsControl))
        {
            throw new ArgumentException("Dart target is required.", nameof(target));
        }

        var normalized = target.Trim().Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal)) normalized = normalized[2..];
        if (normalized.StartsWith('/', StringComparison.Ordinal) || normalized.Contains("../", StringComparison.Ordinal) || normalized.Length > 260)
        {
            throw new ArgumentException("Dart target must be a bounded project-relative path.", nameof(target));
        }

        if (!normalized.EndsWith(".dart", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Dart target must reference a .dart file.", nameof(target));
        }

        return normalized;
    }

    public static PlannedArtifactKind ExpectedArtifact(PlannedBuildMode mode, PlannedArtifactKind requested)
    {
        if (requested == PlannedArtifactKind.Aab && mode != PlannedBuildMode.Release)
        {
            throw new InvalidOperationException("AAB artifacts are release-only.");
        }

        return requested;
    }

    private static PlannedBuildVariant Normalize(BuildVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        var artifact = ExpectedArtifact(variant.Mode, variant.Artifact);
        var retryBudget = variant.RetryBudget ?? (variant.Mode == PlannedBuildMode.Release ? 1 : 0);
        if (retryBudget is < 0 or > MaxRetries)
        {
            throw new ArgumentOutOfRangeException(nameof(variant), $"Retry budget must be 0..{MaxRetries}.");
        }

        return new PlannedBuildVariant(
            NormalizeFlavor(variant.Flavor),
            NormalizeTarget(variant.Target),
            variant.Mode,
            artifact,
            retryBudget);
    }

    private static string CanonicalKey(PlannedBuildVariant variant) =>
        $"{(int)variant.Mode}|{(int)variant.Artifact}|{variant.Flavor ?? string.Empty}|{variant.Target}|{variant.RetryBudget}";

    private static string Fingerprint(IEnumerable<PlannedBuildVariant> variants)
    {
        var canonical = string.Join("\n", variants.Select(CanonicalKey));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
