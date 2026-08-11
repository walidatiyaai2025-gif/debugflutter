using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record ReleaseNoteEntry(string Identity, string Category, string Summary);

public sealed record ReleaseNotesDecision(
    string ReleaseIdentity,
    string Version,
    IReadOnlyList<ReleaseNoteEntry> Notes,
    string ReasonCode,
    string Fingerprint);

public static class ReleaseNotesIntegrityPolicy
{
    public const int DefaultMaxNotes = 100;
    public const int DefaultMaxSummaryLength = 240;
    private static readonly Regex IdentityPattern = new("^[a-z0-9][a-z0-9._-]{0,127}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SemVerPattern = new("^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(?:-[0-9a-z.-]+)?(?:\\+[0-9a-z.-]+)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex WhitespacePattern = new("\\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static ReleaseNotesDecision Evaluate(
        string releaseIdentity,
        string version,
        IEnumerable<ReleaseNoteEntry> notes,
        int maxNotes = DefaultMaxNotes,
        int maxSummaryLength = DefaultMaxSummaryLength)
    {
        ArgumentNullException.ThrowIfNull(notes);
        var identity = NormalizeIdentity(releaseIdentity, "release identity");
        var normalizedVersion = NormalizeVersion(version);
        maxNotes = Math.Clamp(maxNotes, 1, 500);
        maxSummaryLength = Math.Clamp(maxSummaryLength, 40, 1000);

        var normalized = notes.Select(note => NormalizeNote(note, maxSummaryLength)).ToArray();
        if (normalized.Length > maxNotes)
        {
            throw new ArgumentOutOfRangeException(nameof(notes), $"Release notes exceed the {maxNotes} note limit.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var note in normalized)
        {
            if (!seen.Add(note.Identity))
            {
                throw new ArgumentException($"Duplicate release-note identity '{note.Identity}'.", nameof(notes));
            }
        }

        var ordered = normalized.OrderBy(note => note.Category, StringComparer.Ordinal)
            .ThenBy(note => note.Identity, StringComparer.Ordinal)
            .ToArray();
        var canonical = string.Join("\n", ordered.Select(note => $"{note.Identity}|{note.Category}|{note.Summary}"));
        return new ReleaseNotesDecision(identity, normalizedVersion, ordered, "release-notes-valid", Hash($"{identity}|{normalizedVersion}\n{canonical}"));
    }

    private static ReleaseNoteEntry NormalizeNote(ReleaseNoteEntry note, int maxSummaryLength)
    {
        ArgumentNullException.ThrowIfNull(note);
        var identity = NormalizeIdentity(note.Identity, "release-note identity");
        var category = NormalizeIdentity(note.Category, "release-note category");
        if (string.IsNullOrWhiteSpace(note.Summary))
        {
            throw new ArgumentException("Release-note summary is required.", nameof(note));
        }

        if (note.Summary.Any(char.IsControl))
        {
            throw new ArgumentException("Release-note summary contains control characters.", nameof(note));
        }

        var summary = WhitespacePattern.Replace(note.Summary.Trim(), " ");
        if (summary.Length > maxSummaryLength)
        {
            summary = summary[..maxSummaryLength];
        }

        return new ReleaseNoteEntry(identity, category, summary);
    }

    private static string NormalizeVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Release version is required.", nameof(value));
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (!SemVerPattern.IsMatch(normalized))
        {
            throw new ArgumentException($"Invalid semantic release version '{value}'.", nameof(value));
        }

        return normalized;
    }

    private static string NormalizeIdentity(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{label} is required.", nameof(value));
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (!IdentityPattern.IsMatch(normalized))
        {
            throw new ArgumentException($"Unsafe {label} '{value}'.", nameof(value));
        }

        return normalized;
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
