using FlutterBuildDoctor.Application.Logging;
using FlutterBuildDoctor.Infrastructure.Logging;
using Serilog;

namespace FlutterBuildDoctor.IntegrationTests.Logging;

public sealed class LoggingBaselineTests
{
    [Fact]
    public void AppLogStoreSink_CapturesStructuredEvent()
    {
        var store = new InMemoryAppLogStore();
        var sink = new AppLogStoreSink(store);

        using var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Warning("Build {BuildId} failed", 42);

        var entry = Assert.Single(store.Snapshot());
        Assert.Equal(AppLogLevel.Warning, entry.Level);
        Assert.Equal("Build 42 failed", entry.Message);
        Assert.Equal("42", entry.Properties["BuildId"]);
        Assert.True(entry.Timestamp > DateTimeOffset.MinValue);
    }

    [Fact]
    public void InMemoryAppLogStore_DropsOldestEntriesAtCapacity()
    {
        var store = new InMemoryAppLogStore(capacity: 2);

        store.Append(CreateEntry("first"));
        store.Append(CreateEntry("second"));
        store.Append(CreateEntry("third"));

        var snapshot = store.Snapshot();

        Assert.Equal(2, snapshot.Count);
        Assert.Equal("second", snapshot[0].Message);
        Assert.Equal("third", snapshot[1].Message);
    }

    private static AppLogEntry CreateEntry(string message) => new(
        DateTimeOffset.UtcNow,
        AppLogLevel.Information,
        message,
        Exception: null,
        new Dictionary<string, string>());
}
