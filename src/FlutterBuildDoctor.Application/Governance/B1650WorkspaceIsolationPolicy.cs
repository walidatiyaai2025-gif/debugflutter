using System;
using System.Collections.Generic;
using System.Linq;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record WorkspaceBoundary(string WorkspaceIdentity, string RootPath);
public sealed record WorkspaceIsolationFinding(string WorkspaceIdentity, string Path, string Kind);
public sealed record WorkspaceIsolationDecision(
    IReadOnlyList<WorkspaceBoundary> Workspaces,
    IReadOnlyList<WorkspaceIsolationFinding> Findings,
    bool Isolated,
    string ReasonCode,
    string Fingerprint);

public static class WorkspaceIsolationBoundaryPolicy
{
    public static WorkspaceIsolationDecision Evaluate(IEnumerable<WorkspaceBoundary> workspaces, IEnumerable<(string WorkspaceIdentity, string ChildPath)> children)
    {
        ArgumentNullException.ThrowIfNull(workspaces);
        ArgumentNullException.ThrowIfNull(children);
        var normalized = workspaces.Select(item => new WorkspaceBoundary(
            B1550PolicyHelpers.Identity(item.WorkspaceIdentity, nameof(item.WorkspaceIdentity)),
            B1550PolicyHelpers.RelativePath(item.RootPath, nameof(item.RootPath)))).OrderBy(item => item.WorkspaceIdentity, StringComparer.Ordinal).ToArray();

        if (normalized.GroupBy(item => item.WorkspaceIdentity, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new ArgumentException("Duplicate workspace identities are not allowed.", nameof(workspaces));

        var map = normalized.ToDictionary(item => item.WorkspaceIdentity, StringComparer.Ordinal);
        var findings = new List<WorkspaceIsolationFinding>();

        foreach (var child in children)
        {
            var id = B1550PolicyHelpers.Identity(child.WorkspaceIdentity, nameof(child.WorkspaceIdentity));
            if (!map.TryGetValue(id, out var workspace)) throw new ArgumentException("Unknown workspace identity.", nameof(children));
            var path = B1550PolicyHelpers.RelativePath(child.ChildPath, nameof(child.ChildPath));
            var rootPrefix = workspace.RootPath + "/";
            var inside = path.Equals(workspace.RootPath, StringComparison.OrdinalIgnoreCase) || path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
            if (!inside) findings.Add(new WorkspaceIsolationFinding(id, path, "child-outside-root"));
        }

        for (var i = 0; i < normalized.Length; i++)
        for (var j = i + 1; j < normalized.Length; j++)
        {
            var left = normalized[i];
            var right = normalized[j];
            var overlap = left.RootPath.Equals(right.RootPath, StringComparison.OrdinalIgnoreCase)
                || left.RootPath.StartsWith(right.RootPath + "/", StringComparison.OrdinalIgnoreCase)
                || right.RootPath.StartsWith(left.RootPath + "/", StringComparison.OrdinalIgnoreCase);
            if (overlap)
            {
                findings.Add(new WorkspaceIsolationFinding(left.WorkspaceIdentity, left.RootPath, "workspace-overlap"));
                findings.Add(new WorkspaceIsolationFinding(right.WorkspaceIdentity, right.RootPath, "workspace-overlap"));
            }
        }

        var ordered = findings.OrderBy(item => item.WorkspaceIdentity, StringComparer.Ordinal).ThenBy(item => item.Kind, StringComparer.Ordinal).ThenBy(item => item.Path, StringComparer.Ordinal).ToArray();
        var isolated = ordered.Length == 0;
        var reason = isolated ? "workspace-isolation-valid" : "workspace-isolation-violation";
        var payload = $"{isolated}|{string.Join(';', normalized.Select(w => $"{w.WorkspaceIdentity}:{w.RootPath}"))}|{string.Join(';', ordered.Select(f => $"{f.WorkspaceIdentity}:{f.Kind}:{f.Path}"))}";
        return new WorkspaceIsolationDecision(normalized, ordered, isolated, reason, B1550PolicyHelpers.Fingerprint(payload));
    }
}
