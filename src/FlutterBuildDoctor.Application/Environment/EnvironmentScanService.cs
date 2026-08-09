using FlutterBuildDoctor.Application.Services;
using FlutterBuildDoctor.Domain.Environment;

namespace FlutterBuildDoctor.Application.Environment;

public sealed class EnvironmentScanService : IEnvironmentScanner
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
            results.Add(await detector.DetectAsync(cancellationToken).ConfigureAwait(false));
        }

        return results;
    }
}
