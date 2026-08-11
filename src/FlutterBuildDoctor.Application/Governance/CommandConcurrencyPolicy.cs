using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record QueuedCommand(
    string Id,
    string Repository,
    string Group,
    bool UserInitiated,
    bool Exclusive,
    long Sequence);

public sealed record CommandConcurrencyDecision(
    int GlobalConcurrency,
    int PerRepositoryConcurrency,
    IReadOnlyList<QueuedCommand> OrderedQueue,
    bool CanStartNext,
    string ReasonCode,
    string Fingerprint);

public static partial class CommandConcurrencyPolicy
{
    public const int MaxQueuedCommands = 512;
    public const int MaxGlobalConcurrency = 16;

    public static CommandConcurrencyDecision Schedule(
        IEnumerable<QueuedCommand> queued,
        int globalConcurrency,
        int perRepositoryConcurrency,
        int runningSharedCount = 0,
        bool exclusiveOperationRunning = false)
    {
        ArgumentNullException.ThrowIfNull(queued);
        var input = queued.ToArray();
        if (input.Length > MaxQueuedCommands)
        {
            throw new ArgumentOutOfRangeException(nameof(queued), "Queued command count exceeds the supported bound.");
        }
        if (runningSharedCount < 0) throw new ArgumentOutOfRangeException(nameof(runningSharedCount));

        var global = Math.Clamp(globalConcurrency, 1, MaxGlobalConcurrency);
        var perRepository = Math.Clamp(perRepositoryConcurrency, 1, global);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = input.Select(item => Normalize(item, seen)).ToArray();
        var ordered = normalized
            .OrderByDescending(item => item.UserInitiated)
            .ThenBy(item => item.Sequence)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();

        var next = ordered.FirstOrDefault();
        var canStart = next is not null
            && !exclusiveOperationRunning
            && runningSharedCount < global
            && (!next.Exclusive || runningSharedCount == 0);
        var reason = next is null ? "queue-empty"
            : exclusiveOperationRunning ? "exclusive-running"
            : runningSharedCount >= global ? "global-limit-reached"
            : next.Exclusive && runningSharedCount > 0 ? "exclusive-waits-for-shared"
            : "command-ready";

        var canonical = $"{global}|{perRepository}|{runningSharedCount}|{exclusiveOperationRunning}|{reason}\n" +
            string.Join('\n', ordered.Select(item => $"{item.Id}|{item.Repository}|{item.Group}|{item.UserInitiated}|{item.Exclusive}|{item.Sequence}"));
        return new CommandConcurrencyDecision(global, perRepository, ordered, canStart, reason, Hash(canonical));
    }

    private static QueuedCommand Normalize(QueuedCommand item, ISet<string> seen)
    {
        ArgumentNullException.ThrowIfNull(item);
        var id = NormalizeToken(item.Id, nameof(item.Id));
        if (!seen.Add(id)) throw new ArgumentException($"Duplicate queued command '{id}'.", nameof(item));
        var repository = NormalizeToken(item.Repository, nameof(item.Repository));
        var group = NormalizeToken(item.Group, nameof(item.Group));
        if (item.Sequence < 0) throw new ArgumentOutOfRangeException(nameof(item), "Command sequence cannot be negative.");
        return item with { Id = id, Repository = repository, Group = group };
    }

    public static string NormalizeToken(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (!TokenRegex().IsMatch(normalized)) throw new ArgumentException("Command concurrency identity is invalid.", parameterName);
        return normalized;
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [GeneratedRegex("^[a-z0-9][a-z0-9._/-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();
}
