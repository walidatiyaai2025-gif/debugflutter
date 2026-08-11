namespace FlutterBuildDoctor.Application.Intelligence;

public enum ProblemSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Blocker = 3
}

public sealed record SuggestedAction(string Id, int Priority = 0);

public sealed record ProblemEvidence(
    string Signature,
    string Message,
    DateTimeOffset SeenAt,
    int Confidence = 100,
    string? Component = null,
    bool Actionable = true,
    IReadOnlyList<SuggestedAction>? Actions = null);

public sealed record ProblemCluster(
    string Signature,
    ProblemSeverity Severity,
    string Component,
    int Occurrences,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc,
    int Confidence,
    bool Actionable,
    IReadOnlyList<SuggestedAction> SuggestedActions);

public static class ProblemIntelligence
{
    public static IReadOnlyList<ProblemCluster> Analyze(IEnumerable<ProblemEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var normalized = evidence.Select(Normalize).ToArray();

        return normalized
            .GroupBy(item => item.Signature, StringComparer.Ordinal)
            .Select(BuildCluster)
            .OrderByDescending(cluster => cluster.Severity)
            .ThenBy(cluster => cluster.Signature, StringComparer.Ordinal)
            .ToArray();
    }

    public static string NormalizeSignature(string signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            throw new ArgumentException("Problem signature is required.", nameof(signature));
        }

        var normalized = string.Join('-', signature
            .Trim()
            .ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (normalized.Length > 160 || normalized.Any(char.IsControl))
        {
            throw new ArgumentException("Problem signature is invalid.", nameof(signature));
        }

        return normalized;
    }

    public static ProblemSeverity ClassifySeverity(string message)
    {
        var text = RequireMessage(message).ToLowerInvariant();
        if (ContainsAny(text, "blocker", "fatal", "cannot continue")) return ProblemSeverity.Blocker;
        if (ContainsAny(text, "error", "failed", "failure", "exception")) return ProblemSeverity.Error;
        if (ContainsAny(text, "warning", "deprecated", "outdated")) return ProblemSeverity.Warning;
        return ProblemSeverity.Info;
    }

    public static string InferComponent(string message, string? explicitComponent = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitComponent))
        {
            var component = explicitComponent.Trim().ToLowerInvariant();
            if (component.Length > 64 || component.Any(char.IsControl))
            {
                throw new ArgumentException("Problem component is invalid.", nameof(explicitComponent));
            }

            return component;
        }

        var text = RequireMessage(message).ToLowerInvariant();
        if (text.Contains("flutter", StringComparison.Ordinal)) return "flutter";
        if (text.Contains("gradle", StringComparison.Ordinal)) return "gradle";
        if (text.Contains("android", StringComparison.Ordinal) || text.Contains("adb", StringComparison.Ordinal)) return "android";
        if (text.Contains("java", StringComparison.Ordinal) || text.Contains("jdk", StringComparison.Ordinal)) return "java";
        if (text.Contains("kotlin", StringComparison.Ordinal)) return "kotlin";
        if (text.Contains("git", StringComparison.Ordinal)) return "git";
        return "general";
    }

    private static ProblemEvidence Normalize(ProblemEvidence item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item with
        {
            Signature = NormalizeSignature(item.Signature),
            Message = RequireMessage(item.Message),
            SeenAt = item.SeenAt.ToUniversalTime(),
            Confidence = Math.Clamp(item.Confidence, 0, 100),
            Component = InferComponent(item.Message, item.Component)
        };
    }

    private static ProblemCluster BuildCluster(IGrouping<string, ProblemEvidence> group)
    {
        var items = group.ToArray();
        var actions = items
            .SelectMany(item => item.Actions ?? Array.Empty<SuggestedAction>())
            .Where(action => !string.IsNullOrWhiteSpace(action.Id) && !action.Id.Any(char.IsControl))
            .GroupBy(action => action.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(actionGroup => new SuggestedAction(actionGroup.First().Id.Trim(), actionGroup.Max(action => action.Priority)))
            .OrderByDescending(action => action.Priority)
            .ThenBy(action => action.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ProblemCluster(
            group.Key,
            items.Max(item => ClassifySeverity(item.Message)),
            items.GroupBy(item => item.Component!, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(componentGroup => componentGroup.Count())
                .ThenBy(componentGroup => componentGroup.Key, StringComparer.OrdinalIgnoreCase)
                .First().Key,
            items.Length,
            items.Min(item => item.SeenAt),
            items.Max(item => item.SeenAt),
            (int)Math.Round(items.Average(item => item.Confidence), MidpointRounding.AwayFromZero),
            items.Any(item => item.Actionable),
            actions);
    }

    private static string RequireMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > 4096)
        {
            throw new ArgumentException("Problem message is required and must be bounded.", nameof(message));
        }

        return message.Trim();
    }

    private static bool ContainsAny(string text, params string[] values) => values.Any(value => text.Contains(value, StringComparison.Ordinal));
}
