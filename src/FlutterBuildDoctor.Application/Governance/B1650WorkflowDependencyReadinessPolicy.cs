using System;
using System.Collections.Generic;
using System.Linq;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record WorkflowDependencyNode(string Identity, IReadOnlyList<string> Dependencies, bool Completed);
public sealed record WorkflowDependencyReadinessDecision(
    IReadOnlyList<string> ReadyWorkflowIds,
    IReadOnlyList<string> BlockedWorkflowIds,
    int CompletedCount,
    string ReasonCode,
    string Fingerprint);

public static class WorkflowDependencyReadinessPolicy
{
    public static WorkflowDependencyReadinessDecision Evaluate(IEnumerable<WorkflowDependencyNode> workflows)
    {
        ArgumentNullException.ThrowIfNull(workflows);
        var normalized = workflows.Select(node =>
        {
            ArgumentNullException.ThrowIfNull(node);
            var identity = B1550PolicyHelpers.Identity(node.Identity, nameof(node.Identity));
            var dependencies = (node.Dependencies ?? Array.Empty<string>())
                .Select(dep => B1550PolicyHelpers.Identity(dep, nameof(node.Dependencies)))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(dep => dep, StringComparer.Ordinal)
                .ToArray();
            if (dependencies.Contains(identity, StringComparer.Ordinal)) throw new ArgumentException("Workflow cannot depend on itself.", nameof(workflows));
            return new WorkflowDependencyNode(identity, dependencies, node.Completed);
        }).OrderBy(node => node.Identity, StringComparer.Ordinal).ToArray();

        if (normalized.GroupBy(node => node.Identity, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new ArgumentException("Duplicate workflow identities are not allowed.", nameof(workflows));

        var map = normalized.ToDictionary(node => node.Identity, StringComparer.Ordinal);
        foreach (var node in normalized)
            foreach (var dependency in node.Dependencies)
                if (!map.ContainsKey(dependency)) throw new ArgumentException($"Unknown workflow dependency '{dependency}'.", nameof(workflows));

        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        bool Visit(string id)
        {
            if (state.TryGetValue(id, out var value)) return value == 1;
            state[id] = 1;
            foreach (var dependency in map[id].Dependencies)
                if (Visit(dependency)) return true;
            state[id] = 2;
            return false;
        }
        foreach (var id in map.Keys)
            if (Visit(id)) throw new ArgumentException("Workflow dependency graph contains a cycle.", nameof(workflows));

        var ready = normalized.Where(node => !node.Completed && node.Dependencies.All(dep => map[dep].Completed)).Select(node => node.Identity).ToArray();
        var blocked = normalized.Where(node => !node.Completed && !node.Dependencies.All(dep => map[dep].Completed)).Select(node => node.Identity).ToArray();
        var completed = normalized.Count(node => node.Completed);
        var reason = blocked.Length == 0 ? "workflow-dependencies-ready" : "workflow-dependencies-blocked";
        var payload = $"{completed}|{string.Join(',', ready)}|{string.Join(',', blocked)}|{string.Join(';', normalized.Select(node => $"{node.Identity}:{node.Completed}:{string.Join('+', node.Dependencies)}"))}";
        return new WorkflowDependencyReadinessDecision(ready, blocked, completed, reason, B1550PolicyHelpers.Fingerprint(payload));
    }
}
