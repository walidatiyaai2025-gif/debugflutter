using System.Security.Cryptography;
using System.Text;

namespace FlutterBuildDoctor.Application.Environment;

public enum SdkCandidateSource
{
    Explicit = 0,
    LocalProperties = 1,
    Environment = 2,
    Discovery = 3
}

public sealed record SdkPathCandidate(string Path, SdkCandidateSource Source, bool Exists);

public sealed record SdkPathResolution(
    SdkPathCandidate? Selected,
    IReadOnlyList<SdkPathCandidate> Candidates,
    string ReasonCode,
    string Fingerprint);

public static class SdkPathResolutionPolicy
{
    public const int MaxCandidates = 32;

    public static SdkPathResolution Resolve(
        IEnumerable<SdkPathCandidate> candidates,
        IEnumerable<string>? approvedRoots = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var materialized = candidates.ToList();
        if (materialized.Count > MaxCandidates)
        {
            throw new ArgumentOutOfRangeException(nameof(candidates), "SDK candidate count exceeds the supported bound.");
        }

        var roots = NormalizeRoots(approvedRoots ?? Array.Empty<string>());
        var normalized = new Dictionary<string, SdkPathCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in materialized)
        {
            var path = NormalizePath(candidate.Path);
            if (roots.Count > 0 && !roots.Any(root => IsWithinRoot(path, root)))
            {
                throw new ArgumentException("SDK candidate is outside approved roots.", nameof(candidates));
            }

            var normalizedCandidate = candidate with { Path = path };
            if (!normalized.TryGetValue(path, out var current)
                || Rank(normalizedCandidate) < Rank(current))
            {
                normalized[path] = normalizedCandidate;
            }
        }

        var ordered = normalized.Values
            .OrderBy(item => item.Source)
            .ThenByDescending(item => item.Exists)
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var selected = ordered.FirstOrDefault(item => item.Exists) ?? ordered.FirstOrDefault();
        var reason = selected is null
            ? "no-candidates"
            : selected.Exists
                ? selected.Source == SdkCandidateSource.Explicit ? "explicit-existing" : "best-existing"
                : "selected-missing";

        var canonical = string.Join('\n', ordered.Select(item => $"{item.Source}|{item.Exists}|{item.Path}")) + $"\nreason={reason}";
        return new SdkPathResolution(selected, ordered, reason, Hash(canonical));
    }

    public static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var trimmed = path.Trim();
        if (!Path.IsPathFullyQualified(trimmed))
        {
            throw new ArgumentException("SDK path must be fully qualified.", nameof(path));
        }
        return Path.GetFullPath(trimmed).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static IReadOnlyList<string> NormalizeRoots(IEnumerable<string> roots)
        => roots.Select(NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();

    private static bool IsWithinRoot(string path, string root)
        => path.Equals(root, StringComparison.OrdinalIgnoreCase)
           || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
           || path.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static int Rank(SdkPathCandidate candidate) => ((int)candidate.Source * 2) + (candidate.Exists ? 0 : 1);

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
