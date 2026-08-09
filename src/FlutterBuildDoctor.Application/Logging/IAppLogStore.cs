namespace FlutterBuildDoctor.Application.Logging;

public interface IAppLogStore
{
    event Action<AppLogEntry>? EntryAdded;

    IReadOnlyList<AppLogEntry> Snapshot();
}
