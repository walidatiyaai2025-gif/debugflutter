namespace FlutterBuildDoctor.Flutter.Release;

public sealed class ReleasePreflightService : IReleasePreflightService
{
    private readonly IReleasePackageInspector _packageInspector;
    private readonly IReleaseVersionInspector _versionInspector;
    private readonly IReleaseSigningInspector _signingInspector;
    private readonly IReleaseManifestInspector _manifestInspector;

    public ReleasePreflightService(
        IReleasePackageInspector packageInspector,
        IReleaseVersionInspector versionInspector,
        IReleaseSigningInspector signingInspector,
        IReleaseManifestInspector manifestInspector)
    {
        _packageInspector = packageInspector ?? throw new ArgumentNullException(nameof(packageInspector));
        _versionInspector = versionInspector ?? throw new ArgumentNullException(nameof(versionInspector));
        _signingInspector = signingInspector ?? throw new ArgumentNullException(nameof(signingInspector));
        _manifestInspector = manifestInspector ?? throw new ArgumentNullException(nameof(manifestInspector));
    }

    public ReleasePreflightReport Inspect(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        var root = Path.GetFullPath(projectRoot);
        return new ReleasePreflightReport(
            root,
            new[]
            {
                _packageInspector.Inspect(root),
                _versionInspector.Inspect(root),
                _signingInspector.Inspect(root),
                _manifestInspector.Inspect(root)
            });
    }
}

public sealed class InMemoryReleaseHistoryStore : IReleaseHistoryStore
{
    private readonly object _gate = new();
    private readonly List<ReleaseReceipt> _history = new();
    private readonly int _capacity;

    public InMemoryReleaseHistoryStore(int capacity = 100)
    {
        if (capacity is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public Task AddAsync(ReleaseReceipt receipt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _history.Insert(0, receipt);
            if (_history.Count > _capacity)
                _history.RemoveRange(_capacity, _history.Count - _capacity);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ReleaseReceipt>> GetRecentAsync(int limit = 20, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<ReleaseReceipt>>(_history.Take(limit).ToArray());
        }
    }
}
