using System.Security.Cryptography;
using System.Text;

namespace FlutterBuildDoctor.Application.Archives;

public sealed record ArchiveEntryCandidate(string EntryPath, long UncompressedBytes, bool IsLink = false);

public sealed record ArchiveExtractionEntry(string EntryPath, long UncompressedBytes);

public sealed record ArchiveExtractionDecision(
    bool Allowed,
    IReadOnlyList<ArchiveExtractionEntry> Entries,
    long TotalBytes,
    string ReasonCode,
    string Fingerprint);

public static class ArchiveExtractionPolicy
{
    public const int MaxFiles = 10_000;
    public const long MaxTotalBytes = 8L * 1024 * 1024 * 1024;

    public static ArchiveExtractionDecision Evaluate(IEnumerable<ArchiveEntryCandidate> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var input = entries.ToArray();
        if (input.Length > MaxFiles)
        {
            throw new ArgumentOutOfRangeException(nameof(entries), "Archive file count exceeds the extraction bound.");
        }

        var normalized = new List<ArchiveExtractionEntry>(input.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in input)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (entry.IsLink)
            {
                throw new ArgumentException("Archive symbolic-link/reparse entries are not allowed.", nameof(entries));
            }
            if (entry.UncompressedBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(entries), "Archive entry size cannot be negative.");
            }

            var path = NormalizeEntryPath(entry.EntryPath);
            if (!seen.Add(path))
            {
                throw new ArgumentException("Archive contains duplicate destination paths.", nameof(entries));
            }

            checked { total += entry.UncompressedBytes; }
            if (total > MaxTotalBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(entries), "Archive expanded size exceeds the extraction bound.");
            }
            normalized.Add(new ArchiveExtractionEntry(path, entry.UncompressedBytes));
        }

        var ordered = normalized.OrderBy(item => item.EntryPath, StringComparer.Ordinal).ToArray();
        var canonical = string.Join('\n', ordered.Select(item => $"{item.EntryPath}|{item.UncompressedBytes}"));
        return new ArchiveExtractionDecision(true, ordered, total, "extraction-approved", Hash(canonical));
    }

    public static string NormalizeEntryPath(string entryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPath);
        var value = entryPath.Trim().Replace('\\', '/');
        if (value.StartsWith('/', StringComparison.Ordinal)
            || Path.IsPathRooted(value)
            || value.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException("Archive entry must be relative and drive-independent.", nameof(entryPath));
        }

        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException("Archive entry contains path traversal.", nameof(entryPath));
        }

        return string.Join('/', segments);
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
