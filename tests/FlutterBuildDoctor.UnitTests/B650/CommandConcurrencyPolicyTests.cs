using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B650;

public sealed class CommandConcurrencyPolicyTests
{
    [Fact]
    public void Schedule_OrdersInteractiveWorkBeforeAutomaticWork()
    {
        var items = new[]
        {
            new QueuedCommand("automatic", "repo", "build", false, false, 1),
            new QueuedCommand("interactive-2", "repo", "doctor", true, false, 2),
            new QueuedCommand("interactive-1", "repo", "doctor", true, false, 1)
        };

        var result = CommandConcurrencyPolicy.Schedule(items, 100, 100);

        Assert.Equal(16, result.GlobalConcurrency);
        Assert.Equal(new[] { "interactive-1", "interactive-2", "automatic" }, result.OrderedQueue.Select(x => x.Id));
        Assert.True(result.CanStartNext);
    }

    [Fact]
    public void Schedule_ExclusiveWorkWaitsForRunningSharedWork()
    {
        var items = new[] { new QueuedCommand("repair", "repo", "repair", true, true, 0) };
        var result = CommandConcurrencyPolicy.Schedule(items, 4, 2, runningSharedCount: 1);
        Assert.False(result.CanStartNext);
        Assert.Equal("exclusive-waits-for-shared", result.ReasonCode);
    }

    [Fact]
    public void Schedule_ActiveExclusiveWorkBlocksNextItem()
    {
        var items = new[] { new QueuedCommand("scan", "repo", "doctor", true, false, 0) };
        var result = CommandConcurrencyPolicy.Schedule(items, 4, 2, exclusiveOperationRunning: true);
        Assert.False(result.CanStartNext);
        Assert.Equal("exclusive-running", result.ReasonCode);
    }

    [Fact]
    public void Schedule_FingerprintDoesNotDependOnInputOrder()
    {
        var items = new[]
        {
            new QueuedCommand("b", "repo", "build", false, false, 2),
            new QueuedCommand("a", "repo", "build", false, false, 1)
        };
        var first = CommandConcurrencyPolicy.Schedule(items, 4, 2);
        var second = CommandConcurrencyPolicy.Schedule(items.AsEnumerable().Reverse(), 4, 2);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void Schedule_RejectsDuplicateIdentifiers()
        => Assert.Throws<ArgumentException>(() => CommandConcurrencyPolicy.Schedule(new[]
        {
            new QueuedCommand("same", "repo", "build", false, false, 1),
            new QueuedCommand("SAME", "repo", "build", false, false, 2)
        }, 2, 1));
}
