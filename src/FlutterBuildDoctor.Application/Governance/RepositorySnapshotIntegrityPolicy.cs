using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record RepositorySnapshotFile(string RelativePath, string Sha256);

public sealed record RepositorySnapshotDecision(
    string Identity,
    string RepositoryRoot,
    DateTimeOffset SnapshotAtUtc,
    IReadOnlyList<RepositorySnapshotFile> Files,
    string ReasonCode,
    string Fingerprint);

public static partial class RepositorySnapshotIntegrityPolicy
{
    public const int MaxTrackedFiles = 200_000;

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentityPattern();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex HashPattern();

    public static RepositorySnapshotDecision Evaluate(
        string identity,
        string repositoryRoot,
        DateTimeOffset snapshotAt,
        IEnumerable<RepositorySnapshotFile> files)
    {
        var normalizedIdentity = NormalizeIdentity(identity);
        var root = NormalizeRoot(repositoryRoot);
        ArgumentNullException.ThrowIfNull(files);
        var input = files.ToArray();
        if (input.Length > MaxTrackedFiles)
        {
            throw new ArgumentOutOfRangeException(nameof(files), "Tracked-file count exceeds the supported bound.");
        }

        var normalized = input.Select(NormalizeFile).ToArray();
        var duplicate = normalized.GroupBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException("Snapshot contains duplicate tracked paths.", nameof(files));
        }

        var ordered = normalized.OrderBy(file => file.RelativePath, StringComparer.Ordinal).ToArray();
        var timestamp = snapshotAt.ToUniversalTime();
        var canonical = string.Join('\n', ordered.Select(file => $"{file.RelativePath}|{file.Sha256}"));
        var fingerprint = Hash($"{normalizedIdentity}|{root}|{timestamp:O}|{canonical}");
        return new RepositorySnapshotDecision(normalizedIdentity, root, timestamp, ordered, "repository-snapshot-valid", fingerprint);
    }

    public static string NormalizeRelativePath(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().Replace('\\', '/');
        if (Path.IsPathRooted(normalized) || normalized.StartsWith('/') || normalized.Contains(':'))
        {
            throw new ArgumentException("Tracked-file path must be relative.", nameof(value));
        }
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length is < 1 or > 64 || segments.Any(segment => segment is "." or ".." || segment.Any(char.IsControl)))
        {
            throw new ArgumentException("Tracked-file path is invalid.", nameof(value));
        }
        return string.Join('/', segments);
    }

    private static RepositorySnapshotFile NormalizeFile(RepositorySnapshotFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var path = NormalizeRelativePath(file.RelativePath);
        var hash = file.Sha256.Trim().ToLowerInvariant();
        if (!HashPattern().IsMatch(hash))
        {
            throw new ArgumentException("Tracked-file hash must be SHA-256.", nameof(file));
        }
        return new RepositorySnapshotFile(path, hash);
    }

    private static string NormalizeIdentity(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (!IdentityPattern().IsMatch(normalized))
        {
            throw new ArgumentException("Snapshot identity is invalid.", nameof(value));
        }
        return normalized;
    }

    private static string NormalizeRoot(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!Path.IsPathRooted(value))
        {
            throw new ArgumentException("Repository root must be absolute.", nameof(value));
        }
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
