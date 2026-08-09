namespace FlutterBuildDoctor.Application.Logging;

public interface IAppLogStore
{
    event EventHandler<AppLogEntry>? EntryAdded;

    IReadOnlyList<AppLogEntry> Snapshot();
}
