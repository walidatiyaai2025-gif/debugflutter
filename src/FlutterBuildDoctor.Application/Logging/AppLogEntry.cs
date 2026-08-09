namespace FlutterBuildDoctor.Application.Logging;

public sealed record AppLogEntry(
    DateTimeOffset Timestamp,
    AppLogLevel Level,
    string Message,
    string? Exception,
    IReadOnlyDictionary<string, string> Properties);
