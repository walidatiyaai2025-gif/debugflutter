using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Diagnostics;

public sealed record DiagnosticSnapshot(string SnapshotId, DateTimeOffset CapturedAt, IReadOnlyDictionary<string, string> Facts);
public enum DiagnosticDiffKind { Added = 0, Removed = 1, Changed = 2 }
public sealed record DiagnosticDiffItem(string Key, DiagnosticDiffKind Kind, string? Before, string? After);
public sealed record DiagnosticSnapshotDiff(
    string FromSnapshotId,
    string ToSnapshotId,
    DateTimeOffset FromCapturedAtUtc,
    DateTimeOffset ToCapturedAtUtc,
    IReadOnlyList<DiagnosticDiffItem> Items,
    string Summary,
    string Fingerprint);

public static partial class DiagnosticSnapshotDiffPolicy
{
    public const int MaxDiffItems = 500;
    public const int MaxFacts = 2_000;

    public static DiagnosticSnapshotDiff Compare(DiagnosticSnapshot before, DiagnosticSnapshot after)
    {
        var left = Normalize(before);
        var right = Normalize(after);
        var keys = left.Facts.Keys.Concat(right.Facts.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(key => key, StringComparer.Ordinal).ToArray();
        var items = new List<DiagnosticDiffItem>();

        foreach (var key in keys)
        {
            var hasBefore = left.Facts.TryGetValue(key, out var beforeValue);
            var hasAfter = right.Facts.TryGetValue(key, out var afterValue);
            if (!hasBefore)
            {
                items.Add(new DiagnosticDiffItem(key, DiagnosticDiffKind.Added, null, afterValue));
            }
            else if (!hasAfter)
            {
                items.Add(new DiagnosticDiffItem(key, DiagnosticDiffKind.Removed, beforeValue, null));
            }
            else if (!string.Equals(beforeValue, afterValue, StringComparison.Ordinal))
            {
                items.Add(new DiagnosticDiffItem(key, DiagnosticDiffKind.Changed, beforeValue, afterValue));
            }

            if (items.Count > MaxDiffItems)
            {
                throw new ArgumentOutOfRangeException(nameof(after), "Diagnostic diff exceeds the supported bound.");
            }
        }

        var ordered = items.OrderBy(item => item.Key, StringComparer.Ordinal).ThenBy(item => item.Kind).ToArray();
        var added = ordered.Count(item => item.Kind == DiagnosticDiffKind.Added);
        var removed = ordered.Count(item => item.Kind == DiagnosticDiffKind.Removed);
        var changed = ordered.Count(item => item.Kind == DiagnosticDiffKind.Changed);
        var summary = $"added:{added};removed:{removed};changed:{changed}";
        var canonical = string.Join('|', left.SnapshotId, right.SnapshotId, left.CapturedAt.ToString("O"), right.CapturedAt.ToString("O"), summary,
            string.Join(';', ordered.Select(item => $"{item.Key}:{item.Kind}:{item.Before}:{item.After}")));
        return new DiagnosticSnapshotDiff(left.SnapshotId, right.SnapshotId, left.CapturedAt, right.CapturedAt, ordered, summary, Hash(canonical));
    }

    public static DiagnosticSnapshot Normalize(DiagnosticSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var id = NormalizeIdentity(snapshot.SnapshotId);
        ArgumentNullException.ThrowIfNull(snapshot.Facts);
        if (snapshot.Facts.Count > MaxFacts)
        {
            throw new ArgumentOutOfRangeException(nameof(snapshot), "Diagnostic snapshot contains too many facts.");
        }

        var facts = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in snapshot.Facts)
        {
            var key = NormalizeFactKey(pair.Key);
            var value = NormalizeFactValue(pair.Value);
            if (!facts.TryAdd(key, value))
            {
                throw new ArgumentException("Diagnostic snapshot contains duplicate normalized fact keys.", nameof(snapshot));
            }
        }
        return new DiagnosticSnapshot(id, snapshot.CapturedAt.ToUniversalTime(), facts);
    }

    private static string NormalizeIdentity(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (!IdentityRegex().IsMatch(normalized)) throw new ArgumentException("Snapshot identity is invalid.", nameof(value));
        return normalized;
    }

    private static string NormalizeFactKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (!FactKeyRegex().IsMatch(normalized)) throw new ArgumentException("Diagnostic fact key is invalid.", nameof(value));
        return normalized;
    }

    private static string NormalizeFactValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Any(char.IsControl)) throw new ArgumentException("Diagnostic fact value contains control characters.", nameof(value));
        var normalized = value.Trim();
        if (normalized.Length > 2048) throw new ArgumentOutOfRangeException(nameof(value), "Diagnostic fact value exceeds the supported bound.");
        return normalized;
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [GeneratedRegex("^[A-Za-z0-9_.:-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentityRegex();

    [GeneratedRegex("^[a-z0-9][a-z0-9_.:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex FactKeyRegex();
}
