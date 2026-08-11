using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record LockedDependency(string Name, string Version, string? Sha256 = null);

public sealed record DependencyLockfileDecision(
    string Identity,
    IReadOnlyList<LockedDependency> Dependencies,
    bool StableOnly,
    bool ChecksumsRequired,
    string ReasonCode,
    string Fingerprint);

public static partial class DependencyLockfilePolicy
{
    public const int MaxDependencies = 2048;

    public static DependencyLockfileDecision Evaluate(
        string identity,
        IEnumerable<LockedDependency> dependencies,
        bool stableOnly = false,
        bool requireChecksums = false)
    {
        var normalizedIdentity = NormalizeIdentity(identity);
        ArgumentNullException.ThrowIfNull(dependencies);
        var input = dependencies.ToArray();
        if (input.Length > MaxDependencies)
        {
            throw new ArgumentOutOfRangeException(nameof(dependencies), "Dependency count exceeds the supported lockfile bound.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<LockedDependency>(input.Length);
        foreach (var dependency in input)
        {
            ArgumentNullException.ThrowIfNull(dependency);
            var name = NormalizeName(dependency.Name);
            if (!seen.Add(name))
            {
                throw new ArgumentException($"Duplicate locked dependency '{name}'.", nameof(dependencies));
            }

            var version = NormalizeVersion(dependency.Version);
            if (stableOnly && version.Contains('-', StringComparison.Ordinal))
            {
                throw new ArgumentException($"Prerelease version '{version}' is not permitted in a stable-only lockfile.", nameof(dependencies));
            }

            var checksum = NormalizeChecksum(dependency.Sha256);
            if (requireChecksums && checksum is null)
            {
                throw new ArgumentException($"Dependency '{name}' requires SHA-256 evidence.", nameof(dependencies));
            }

            normalized.Add(new LockedDependency(name, version, checksum));
        }

        var ordered = normalized.OrderBy(item => item.Name, StringComparer.Ordinal).ToArray();
        var canonical = string.Join('\n', ordered.Select(item => $"{item.Name}|{item.Version}|{item.Sha256 ?? string.Empty}"));
        var fingerprint = Hash($"{normalizedIdentity}|{stableOnly}|{requireChecksums}\n{canonical}");
        return new DependencyLockfileDecision(normalizedIdentity, ordered, stableOnly, requireChecksums, "lockfile-valid", fingerprint);
    }

    public static string NormalizeIdentity(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (!IdentityRegex().IsMatch(normalized))
        {
            throw new ArgumentException("Lockfile identity is invalid.", nameof(value));
        }
        return normalized;
    }

    public static string NormalizeName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (!NameRegex().IsMatch(normalized))
        {
            throw new ArgumentException("Dependency name is invalid.", nameof(value));
        }
        return normalized;
    }

    public static string NormalizeVersion(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (!VersionRegex().IsMatch(normalized))
        {
            throw new ArgumentException("Locked dependency version is invalid.", nameof(value));
        }
        return normalized;
    }

    public static string? NormalizeChecksum(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        if (!Sha256Regex().IsMatch(normalized))
        {
            throw new ArgumentException("SHA-256 checksum is malformed.", nameof(value));
        }
        return normalized;
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentityRegex();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex NameRegex();

    [GeneratedRegex("^[0-9]+(?:\\.[0-9]+){0,3}(?:-[0-9a-z.-]+)?(?:\\+[0-9a-z.-]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();
}
