using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record SessionLineageEntry(string Identity, string? ParentIdentity = null);

public sealed record SessionLineageDecision(
    IReadOnlyList<SessionLineageEntry> Entries,
    IReadOnlyDictionary<string, string> RootBySession,
    IReadOnlyDictionary<string, int> DepthBySession,
    int MaxDepth,
    string ReasonCode,
    string Fingerprint);

public static class SessionLineagePolicy
{
    public const int DefaultMaxDepth = 64;
    private static readonly Regex IdentityPattern = new("^[a-z0-9][a-z0-9._:-]{0,127}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static SessionLineageDecision Evaluate(
        IEnumerable<SessionLineageEntry> entries,
        int maxDepth = DefaultMaxDepth)
    {
        ArgumentNullException.ThrowIfNull(entries);
        maxDepth = Math.Clamp(maxDepth, 1, 256);
        var normalized = entries.Select(Normalize).ToArray();

        var byId = new Dictionary<string, SessionLineageEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in normalized)
        {
            if (!byId.TryAdd(entry.Identity, entry))
            {
                throw new ArgumentException($"Duplicate session identity '{entry.Identity}'.", nameof(entries));
            }

            if (entry.ParentIdentity is not null && string.Equals(entry.Identity, entry.ParentIdentity, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Session '{entry.Identity}' cannot be its own parent.", nameof(entries));
            }
        }

        foreach (var entry in normalized)
        {
            if (entry.ParentIdentity is not null && !byId.ContainsKey(entry.ParentIdentity))
            {
                throw new ArgumentException($"Session '{entry.Identity}' references unknown parent '{entry.ParentIdentity}'.", nameof(entries));
            }
        }

        var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var depths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var identity in byId.Keys.OrderBy(value => value, StringComparer.Ordinal))
        {
            Resolve(identity, byId, roots, depths, maxDepth, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        var ordered = normalized.OrderBy(entry => entry.Identity, StringComparer.Ordinal).ToArray();
        var observedMaxDepth = depths.Count == 0 ? 0 : depths.Values.Max();
        var canonical = string.Join("\n", ordered.Select(entry =>
            $"{entry.Identity}|{entry.ParentIdentity ?? "-"}|{roots[entry.Identity]}|{depths[entry.Identity]}"));
        return new SessionLineageDecision(
            ordered,
            new Dictionary<string, string>(roots, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, int>(depths, StringComparer.OrdinalIgnoreCase),
            observedMaxDepth,
            "session-lineage-valid",
            Hash(canonical));
    }

    private static (string Root, int Depth) Resolve(
        string identity,
        IReadOnlyDictionary<string, SessionLineageEntry> byId,
        IDictionary<string, string> roots,
        IDictionary<string, int> depths,
        int maxDepth,
        ISet<string> visiting)
    {
        if (roots.TryGetValue(identity, out var cachedRoot) && depths.TryGetValue(identity, out var cachedDepth))
        {
            return (cachedRoot, cachedDepth);
        }

        if (!visiting.Add(identity))
        {
            throw new ArgumentException($"Session lineage cycle detected at '{identity}'.", nameof(byId));
        }

        var entry = byId[identity];
        string root;
        int depth;
        if (entry.ParentIdentity is null)
        {
            root = identity;
            depth = 0;
        }
        else
        {
            var parent = Resolve(entry.ParentIdentity, byId, roots, depths, maxDepth, visiting);
            root = parent.Root;
            depth = checked(parent.Depth + 1);
        }

        visiting.Remove(identity);
        if (depth > maxDepth)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDepth), $"Session lineage for '{identity}' exceeds maximum depth {maxDepth}.");
        }

        roots[identity] = root;
        depths[identity] = depth;
        return (root, depth);
    }

    private static SessionLineageEntry Normalize(SessionLineageEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var identity = NormalizeIdentity(entry.Identity);
        var parent = string.IsNullOrWhiteSpace(entry.ParentIdentity) ? null : NormalizeIdentity(entry.ParentIdentity!);
        return new SessionLineageEntry(identity, parent);
    }

    private static string NormalizeIdentity(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Session identity is required.", nameof(value));
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (!IdentityPattern.IsMatch(normalized))
        {
            throw new ArgumentException($"Unsafe session identity '{value}'.", nameof(value));
        }

        return normalized;
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
