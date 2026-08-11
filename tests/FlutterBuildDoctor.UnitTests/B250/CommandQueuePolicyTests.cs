using FlutterBuildDoctor.Application.Commands;

namespace FlutterBuildDoctor.UnitTests.B250;

public sealed class CommandQueuePolicyTests
{
    [Fact]
    public void Create_ValidatesOrdersAndAssignsExecutionSlots()
    {
        var snapshot = CommandQueuePolicy.Create(new[]
        {
            new QueuedCommandSpec("read-b", CommandQueuePriority.Normal, CommandAccessMode.ReadOnly, true),
            new QueuedCommandSpec("mutate", CommandQueuePriority.Critical, CommandAccessMode.Mutating, false),
            new QueuedCommandSpec("read-a", CommandQueuePriority.Normal, CommandAccessMode.ReadOnly, true, RequestedRetries: 2)
        }, parallelReadOnlyLimit: 2);

        Assert.Equal(new[] { "mutate", "read-a", "read-b" }, snapshot.Commands.Select(command => command.Id));
        Assert.True(snapshot.Commands[0].RequiresExclusiveExecution);
        Assert.Equal(-1, snapshot.Commands[0].ExecutionSlot);
        Assert.Equal(TimeSpan.FromMinutes(10), snapshot.Commands[0].Timeout);
        Assert.Equal(0, snapshot.Commands[1].ExecutionSlot);
        Assert.Equal(1, snapshot.Commands[2].ExecutionSlot);
        Assert.Equal(TimeSpan.FromMinutes(2), snapshot.Commands[1].Timeout);
        Assert.Equal(2, snapshot.Commands[1].AllowedRetries);
        Assert.Equal(64, snapshot.Fingerprint.Length);
    }

    [Fact]
    public void Create_RejectsDuplicatesOversizedQueueAndUnsafeRetry()
    {
        Assert.Throws<ArgumentException>(() => CommandQueuePolicy.Create(new[]
        {
            new QueuedCommandSpec("same", CommandQueuePriority.Normal, CommandAccessMode.ReadOnly, true),
            new QueuedCommandSpec("SAME", CommandQueuePriority.High, CommandAccessMode.ReadOnly, true)
        }));

        Assert.Throws<ArgumentOutOfRangeException>(() => CommandQueuePolicy.Create(
            Enumerable.Range(0, CommandQueuePolicy.MaxCommands + 1)
                .Select(index => new QueuedCommandSpec($"cmd-{index}", CommandQueuePriority.Low, CommandAccessMode.ReadOnly, true))));

        Assert.Throws<InvalidOperationException>(() => CommandQueuePolicy.Create(new[]
        {
            new QueuedCommandSpec("unsafe", CommandQueuePriority.High, CommandAccessMode.Mutating, false, RequestedRetries: 1)
        }));
    }

    [Fact]
    public void CancelPending_MarksOnlyRequestedCommandAndChangesDeterministicSnapshot()
    {
        var initial = CommandQueuePolicy.Create(new[]
        {
            new QueuedCommandSpec("a", CommandQueuePriority.Normal, CommandAccessMode.ReadOnly, true),
            new QueuedCommandSpec("b", CommandQueuePriority.Normal, CommandAccessMode.ReadOnly, true)
        });

        var cancelled = CommandQueuePolicy.CancelPending(initial, " B ");

        Assert.False(cancelled.Commands.Single(command => command.Id == "a").Cancelled);
        Assert.True(cancelled.Commands.Single(command => command.Id == "b").Cancelled);
        Assert.NotEqual(initial.Fingerprint, cancelled.Fingerprint);
        Assert.Throws<KeyNotFoundException>(() => CommandQueuePolicy.CancelPending(initial, "missing"));
    }

    [Fact]
    public void Create_RejectsInvalidTimeoutAndParallelLimit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CommandQueuePolicy.Create(
            new[] { new QueuedCommandSpec("a", CommandQueuePriority.Low, CommandAccessMode.ReadOnly, true) },
            parallelReadOnlyLimit: 0));

        Assert.Throws<ArgumentOutOfRangeException>(() => CommandQueuePolicy.Create(new[]
        {
            new QueuedCommandSpec("a", CommandQueuePriority.Low, CommandAccessMode.ReadOnly, true, TimeSpan.FromMilliseconds(10))
        }));
    }
}
