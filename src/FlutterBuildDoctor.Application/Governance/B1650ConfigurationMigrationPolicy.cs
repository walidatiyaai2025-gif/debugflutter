using System;
using System.Collections.Generic;
using System.Linq;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record ConfigurationMigrationStep(string Identity, int FromVersion, int ToVersion);
public sealed record ConfigurationMigrationDecision(
    string ConfigurationIdentity,
    int CurrentVersion,
    int TargetVersion,
    IReadOnlyList<ConfigurationMigrationStep> Plan,
    IReadOnlyList<int> MissingFromVersions,
    bool Ready,
    string ReasonCode,
    string Fingerprint);

public static class ConfigurationMigrationPlanningPolicy
{
    public static ConfigurationMigrationDecision Evaluate(string configurationIdentity, int currentVersion, int targetVersion, IEnumerable<ConfigurationMigrationStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var identity = B1550PolicyHelpers.Identity(configurationIdentity, nameof(configurationIdentity));
        if (currentVersion < 0) throw new ArgumentOutOfRangeException(nameof(currentVersion));
        if (targetVersion < currentVersion) throw new ArgumentException("Target schema version cannot regress.", nameof(targetVersion));

        var normalized = steps.Select(step =>
        {
            ArgumentNullException.ThrowIfNull(step);
            var stepIdentity = B1550PolicyHelpers.Identity(step.Identity, nameof(step.Identity));
            if (step.FromVersion < 0 || step.ToVersion < 0) throw new ArgumentOutOfRangeException(nameof(steps));
            if (step.ToVersion != step.FromVersion + 1) throw new ArgumentException("Migration steps must advance exactly one schema version.", nameof(steps));
            return new ConfigurationMigrationStep(stepIdentity, step.FromVersion, step.ToVersion);
        }).OrderBy(step => step.FromVersion).ThenBy(step => step.Identity, StringComparer.Ordinal).ToArray();

        if (normalized.GroupBy(step => step.Identity, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new ArgumentException("Duplicate migration-step identities are not allowed.", nameof(steps));
        if (normalized.GroupBy(step => step.FromVersion).Any(group => group.Count() > 1))
            throw new ArgumentException("Multiple migration steps cannot start at the same schema version.", nameof(steps));

        var byFrom = normalized.ToDictionary(step => step.FromVersion);
        var plan = new List<ConfigurationMigrationStep>();
        var missing = new List<int>();
        for (var version = currentVersion; version < targetVersion; version++)
        {
            if (byFrom.TryGetValue(version, out var step)) plan.Add(step);
            else missing.Add(version);
        }

        var ready = missing.Count == 0;
        var reason = ready ? "configuration-migration-ready" : "configuration-migration-missing-step";
        var payload = $"{identity}|{currentVersion}|{targetVersion}|{ready}|{string.Join(',', missing)}|{string.Join(';', plan.Select(step => $"{step.Identity}:{step.FromVersion}:{step.ToVersion}"))}";
        return new ConfigurationMigrationDecision(identity, currentVersion, targetVersion, plan, missing, ready, reason, B1550PolicyHelpers.Fingerprint(payload));
    }
}
