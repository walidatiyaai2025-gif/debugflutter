using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Packages;

public sealed record PackageCandidate(string Name, string Version, bool Blocked = false);
public sealed record ResolvedPackage(string Name, string Version, bool Prerelease);
public sealed record PackageResolutionDecision(ResolvedPackage? Selected, IReadOnlyList<ResolvedPackage> Candidates, string ReasonCode, string Fingerprint);

public static partial class PackageResolutionPolicy
{
    public const int MaxCandidates = 200;

    public static PackageResolutionDecision Resolve(string packageName, string versionConstraint, IEnumerable<PackageCandidate> candidates)
    {
        var name = NormalizePackageName(packageName);
        var constraint = NormalizeConstraint(versionConstraint);
        ArgumentNullException.ThrowIfNull(candidates);
        var input = candidates.ToArray();
        if (input.Length > MaxCandidates)
        {
            throw new ArgumentOutOfRangeException(nameof(candidates), "Package candidate count exceeds the supported bound.");
        }

        var compatible = input
            .Select(candidate => NormalizeCandidate(candidate))
            .Where(candidate => candidate.Name == name && !candidate.Blocked)
            .Select(candidate => new { Candidate = candidate, Version = ParsedVersion.Parse(candidate.Version) })
            .Where(item => IsCompatible(item.Version, constraint))
            .OrderBy(item => item.Version.IsPrerelease)
            .ThenByDescending(item => item.Version.Major)
            .ThenByDescending(item => item.Version.Minor)
            .ThenByDescending(item => item.Version.Patch)
            .ThenBy(item => item.Version.Prerelease, StringComparer.Ordinal)
            .Select(item => new ResolvedPackage(item.Candidate.Name, item.Candidate.Version, item.Version.IsPrerelease))
            .ToArray();

        var selected = compatible.FirstOrDefault();
        var reason = selected is null ? "no-compatible-package"
            : constraint.Kind == ConstraintKind.Exact ? "exact-compatible"
            : selected.Prerelease ? "prerelease-fallback"
            : "stable-compatible";
        var canonical = string.Join('|', name, constraint.Canonical, selected?.Version ?? string.Empty, reason, string.Join(',', compatible.Select(item => item.Version)));
        return new PackageResolutionDecision(selected, compatible, reason, Hash(canonical));
    }

    public static string NormalizePackageName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (!PackageNameRegex().IsMatch(normalized))
        {
            throw new ArgumentException("Package name is invalid.", nameof(value));
        }
        return normalized;
    }

    public static string NormalizeVersion(string value) => ParsedVersion.Parse(value).Canonical;

    public static string NormalizeVersionConstraint(string value) => NormalizeConstraint(value).Canonical;

    private static PackageCandidate NormalizeCandidate(PackageCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return candidate with { Name = NormalizePackageName(candidate.Name), Version = NormalizeVersion(candidate.Version) };
    }

    private static Constraint NormalizeConstraint(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (normalized == "*") return new Constraint(ConstraintKind.Any, null, "*");
        if (normalized.StartsWith(">=", StringComparison.Ordinal))
        {
            var version = ParsedVersion.Parse(normalized[2..]);
            return new Constraint(ConstraintKind.Minimum, version, $">={version.Canonical}");
        }
        var exact = ParsedVersion.Parse(normalized);
        return new Constraint(ConstraintKind.Exact, exact, exact.Canonical);
    }

    private static bool IsCompatible(ParsedVersion candidate, Constraint constraint)
        => constraint.Kind switch
        {
            ConstraintKind.Any => true,
            ConstraintKind.Exact => candidate.CompareCore(constraint.Version!) == 0 && string.Equals(candidate.Prerelease, constraint.Version!.Prerelease, StringComparison.OrdinalIgnoreCase),
            ConstraintKind.Minimum => candidate.CompareCore(constraint.Version!) >= 0,
            _ => false
        };

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private enum ConstraintKind { Any, Exact, Minimum }
    private sealed record Constraint(ConstraintKind Kind, ParsedVersion? Version, string Canonical);

    private sealed record ParsedVersion(int Major, int Minor, int Patch, string? Prerelease)
    {
        public bool IsPrerelease => !string.IsNullOrWhiteSpace(Prerelease);
        public string Canonical => $"{Major}.{Minor}.{Patch}" + (IsPrerelease ? $"-{Prerelease!.ToLowerInvariant()}" : string.Empty);

        public int CompareCore(ParsedVersion other)
        {
            var major = Major.CompareTo(other.Major);
            if (major != 0) return major;
            var minor = Minor.CompareTo(other.Minor);
            if (minor != 0) return minor;
            return Patch.CompareTo(other.Patch);
        }

        public static ParsedVersion Parse(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            var match = VersionRegex().Match(value.Trim());
            if (!match.Success) throw new ArgumentException("Package version is invalid.", nameof(value));
            return new ParsedVersion(
                int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture),
                match.Groups[4].Success ? match.Groups[4].Value : null);
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageNameRegex();

    [GeneratedRegex("^(\\d+)\\.(\\d+)\\.(\\d+)(?:-([0-9A-Za-z.-]+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();
}
