using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record DependencyGraphNode(string Name, IReadOnlyCollection<string>? Dependencies = null);

public sealed record DependencyGraphDecision(
    IReadOnlyList<DependencyGraphNode> Nodes,
    IReadOnlyList<string> TopologicalOrder,
    string ReasonCode,
    string Fingerprint);

public static class DependencyGraphIntegrityPolicy
{
    public const int DefaultMaxNodes = 256;
    private static readonly Regex IdentityPattern = new("^[a-z0-9][a-z0-9._-]{0,127}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static DependencyGraphDecision Evaluate(IEnumerable<DependencyGraphNode> nodes, int maxNodes = DefaultMaxNodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        maxNodes = Math.Clamp(maxNodes, 1, 2048);

        var normalized = nodes.Select(NormalizeNode).ToArray();
        if (normalized.Length > maxNodes)
        {
            throw new ArgumentOutOfRangeException(nameof(nodes), $"Dependency graph exceeds the {maxNodes} node limit.");
        }

        var byName = new Dictionary<string, DependencyGraphNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in normalized)
        {
            if (!byName.TryAdd(node.Name, node))
            {
                throw new ArgumentException($"Duplicate dependency node '{node.Name}'.", nameof(nodes));
            }
        }

        foreach (var node in normalized)
        {
            foreach (var dependency in node.Dependencies ?? Array.Empty<string>())
            {
                if (string.Equals(node.Name, dependency, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException($"Dependency node '{node.Name}' cannot depend on itself.", nameof(nodes));
                }

                if (!byName.ContainsKey(dependency))
                {
                    throw new ArgumentException($"Dependency node '{node.Name}' references unknown node '{dependency}'.", nameof(nodes));
                }
            }
        }

        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>(normalized.Length);

        foreach (var name in byName.Keys.OrderBy(value => value, StringComparer.Ordinal))
        {
            Visit(name, byName, visiting, visited, order);
        }

        var orderedNodes = normalized.OrderBy(node => node.Name, StringComparer.Ordinal).ToArray();
        var canonical = string.Join("\n", orderedNodes.Select(node =>
            $"{node.Name}:{string.Join(',', (node.Dependencies ?? Array.Empty<string>()).OrderBy(value => value, StringComparer.Ordinal))}"));

        return new DependencyGraphDecision(
            orderedNodes,
            order,
            "dependency-graph-valid",
            Hash(canonical));
    }

    private static DependencyGraphNode NormalizeNode(DependencyGraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var name = NormalizeIdentity(node.Name);
        var dependencies = (node.Dependencies ?? Array.Empty<string>())
            .Select(NormalizeIdentity)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return new DependencyGraphNode(name, dependencies);
    }

    private static string NormalizeIdentity(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Dependency identity is required.", nameof(value));
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (!IdentityPattern.IsMatch(normalized))
        {
            throw new ArgumentException($"Unsafe dependency identity '{value}'.", nameof(value));
        }

        return normalized;
    }

    private static void Visit(
        string name,
        IReadOnlyDictionary<string, DependencyGraphNode> byName,
        ISet<string> visiting,
        ISet<string> visited,
        ICollection<string> order)
    {
        if (visited.Contains(name))
        {
            return;
        }

        if (!visiting.Add(name))
        {
            throw new ArgumentException($"Dependency cycle detected at '{name}'.", nameof(byName));
        }

        var node = byName[name];
        foreach (var dependency in (node.Dependencies ?? Array.Empty<string>()).OrderBy(value => value, StringComparer.Ordinal))
        {
            Visit(dependency, byName, visiting, visited, order);
        }

        visiting.Remove(name);
        visited.Add(name);
        order.Add(name);
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
