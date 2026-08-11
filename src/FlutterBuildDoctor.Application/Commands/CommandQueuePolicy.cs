using System.Security.Cryptography;
using System.Text;

namespace FlutterBuildDoctor.Application.Commands;

public enum CommandQueuePriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

public enum CommandAccessMode
{
    ReadOnly = 0,
    Mutating = 1
}

public sealed record QueuedCommandSpec(
    string Id,
    CommandQueuePriority Priority,
    CommandAccessMode AccessMode,
    bool Idempotent,
    TimeSpan? Timeout = null,
    int RequestedRetries = 0);

public sealed record PlannedQueuedCommand(
    string Id,
    CommandQueuePriority Priority,
    CommandAccessMode AccessMode,
    bool Idempotent,
    TimeSpan Timeout,
    int AllowedRetries,
    int ExecutionSlot,
    bool RequiresExclusiveExecution,
    bool Cancelled);

public sealed record CommandQueueSnapshot(
    IReadOnlyList<PlannedQueuedCommand> Commands,
    int ParallelReadOnlyLimit,
    string Fingerprint);

public static class CommandQueuePolicy
{
    public const int MaxCommands = 128;
    public const int MaxParallelReadOnly = 8;
    public const int MaxRetries = 2;
    private static readonly TimeSpan MinTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxTimeout = TimeSpan.FromMinutes(30);

    public static CommandQueueSnapshot Create(
        IEnumerable<QueuedCommandSpec> commands,
        int parallelReadOnlyLimit = 2)
    {
        ArgumentNullException.ThrowIfNull(commands);
        if (parallelReadOnlyLimit is < 1 or > MaxParallelReadOnly)
        {
            throw new ArgumentOutOfRangeException(nameof(parallelReadOnlyLimit));
        }

        var materialized = commands.ToArray();
        if (materialized.Length > MaxCommands)
        {
            throw new ArgumentOutOfRangeException(nameof(commands), $"Command queue cannot exceed {MaxCommands} commands.");
        }

        foreach (var command in materialized) ValidateId(command.Id);
        if (materialized.Select(command => command.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != materialized.Length)
        {
            throw new ArgumentException("Queued command IDs must be unique.", nameof(commands));
        }

        var normalized = materialized
            .Select(Normalize)
            .OrderByDescending(command => command.Priority)
            .ThenBy(command => command.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var readOnlyIndex = 0;
        var planned = normalized.Select(command =>
        {
            var exclusive = command.AccessMode == CommandAccessMode.Mutating;
            var slot = exclusive ? -1 : readOnlyIndex++ % parallelReadOnlyLimit;
            return new PlannedQueuedCommand(
                command.Id.Trim(),
                command.Priority,
                command.AccessMode,
                command.Idempotent,
                command.Timeout ?? DefaultTimeout(command.AccessMode),
                command.RequestedRetries,
                slot,
                exclusive,
                Cancelled: false);
        }).ToArray();

        return Snapshot(planned, parallelReadOnlyLimit);
    }

    public static CommandQueueSnapshot CancelPending(CommandQueueSnapshot snapshot, string commandId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateId(commandId);
        var found = false;
        var commands = snapshot.Commands.Select(command =>
        {
            if (!command.Id.Equals(commandId.Trim(), StringComparison.OrdinalIgnoreCase)) return command;
            found = true;
            return command with { Cancelled = true };
        }).ToArray();

        if (!found) throw new KeyNotFoundException($"Queued command '{commandId}' was not found.");
        return Snapshot(commands, snapshot.ParallelReadOnlyLimit);
    }

    private static QueuedCommandSpec Normalize(QueuedCommandSpec command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var timeout = command.Timeout ?? DefaultTimeout(command.AccessMode);
        if (timeout < MinTimeout || timeout > MaxTimeout)
        {
            throw new ArgumentOutOfRangeException(nameof(command), $"Command timeout must be between {MinTimeout} and {MaxTimeout}.");
        }

        if (command.RequestedRetries is < 0 or > MaxRetries)
        {
            throw new ArgumentOutOfRangeException(nameof(command), $"Requested retries must be 0..{MaxRetries}.");
        }

        if (!command.Idempotent && command.RequestedRetries > 0)
        {
            throw new InvalidOperationException("Non-idempotent commands cannot be retried automatically.");
        }

        return command with { Id = command.Id.Trim(), Timeout = timeout };
    }

    private static TimeSpan DefaultTimeout(CommandAccessMode mode) =>
        mode == CommandAccessMode.Mutating ? TimeSpan.FromMinutes(10) : TimeSpan.FromMinutes(2);

    private static CommandQueueSnapshot Snapshot(IReadOnlyList<PlannedQueuedCommand> commands, int parallelReadOnlyLimit)
    {
        var canonical = string.Join("\n", commands.Select(command =>
            $"{command.Id}|{(int)command.Priority}|{(int)command.AccessMode}|{command.Idempotent}|{command.Timeout.Ticks}|{command.AllowedRetries}|{command.ExecutionSlot}|{command.RequiresExclusiveExecution}|{command.Cancelled}"));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return new CommandQueueSnapshot(commands.ToArray(), parallelReadOnlyLimit, fingerprint);
    }

    private static void ValidateId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || id.Any(char.IsControl))
        {
            throw new ArgumentException("Command ID is invalid.", nameof(id));
        }
    }
}
