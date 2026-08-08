namespace FlutterBuildDoctor.Application.Environment;

public interface IToolDetector
{
    string Name { get; }
    Task<ToolStatus> DetectAsync(CancellationToken cancellationToken = default);
}

public sealed record ToolStatus(
    string Name,
    bool IsInstalled,
    string? Version,
    string? Path,
    string Status,
    string? Message);

public sealed class EnvironmentScanService
{
    private readonly IEnumerable<IToolDetector> _detectors;

    public EnvironmentScanService(IEnumerable<IToolDetector> detectors)
    {
        _detectors = detectors;
    }

    public async Task<IReadOnlyList<ToolStatus>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<ToolStatus>();

        foreach (var detector in _detectors)
        {
            results.Add(await detector.DetectAsync(cancellationToken));
        }

        return results;
    }
}
