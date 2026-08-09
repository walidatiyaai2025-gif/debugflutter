using FlutterBuildDoctor.Application.Logging;
using Serilog.Core;
using Serilog.Events;

namespace FlutterBuildDoctor.Infrastructure.Logging;

public sealed class AppLogStoreSink : ILogEventSink
{
    private readonly InMemoryAppLogStore _store;

    public AppLogStoreSink(InMemoryAppLogStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        var properties = logEvent.Properties.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToString(),
            StringComparer.Ordinal);

        _store.Append(new AppLogEntry(
            logEvent.Timestamp,
            MapLevel(logEvent.Level),
            logEvent.RenderMessage(),
            logEvent.Exception?.ToString(),
            properties));
    }

    private static AppLogLevel MapLevel(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => AppLogLevel.Verbose,
        LogEventLevel.Debug => AppLogLevel.Debug,
        LogEventLevel.Information => AppLogLevel.Information,
        LogEventLevel.Warning => AppLogLevel.Warning,
        LogEventLevel.Error => AppLogLevel.Error,
        LogEventLevel.Fatal => AppLogLevel.Fatal,
        _ => AppLogLevel.Information,
    };
}
