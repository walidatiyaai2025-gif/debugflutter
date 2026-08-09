using FlutterBuildDoctor.Application.Logging;

namespace FlutterBuildDoctor.Infrastructure.Logging;

public sealed class InMemoryAppLogStore : IAppLogStore
{
    private readonly object _gate = new();
    private readonly Queue<AppLogEntry> _entries = new();
    private readonly int _capacity;

    public InMemoryAppLogStore(int capacity = 2000)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than zero.");
        }

        _capacity = capacity;
    }

    public event Action<AppLogEntry>? EntryAdded;

    public IReadOnlyList<AppLogEntry> Snapshot()
    {
        lock (_gate)
        {
            return _entries.ToArray();
        }
    }

    public void Append(AppLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_gate)
        {
            _entries.Enqueue(entry);

            while (_entries.Count > _capacity)
            {
                _entries.Dequeue();
            }
        }

        EntryAdded?.Invoke(entry);
    }
}
