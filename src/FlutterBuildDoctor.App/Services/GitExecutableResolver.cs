using FlutterBuildDoctor.Application.Environment;

namespace FlutterBuildDoctor.App.Services;

public sealed record GitExecutableResolution(
    bool IsAvailable,
    string? Path,
    string? Version,
    string Message);

public interface IGitExecutableResolver
{
    Task<GitExecutableResolution> ResolveAsync(CancellationToken cancellationToken = default);
}

public sealed class GitExecutableResolver : IGitExecutableResolver
{
    private readonly IReadOnlyList<IToolDetector> _toolDetectors;

    public GitExecutableResolver(IEnumerable<IToolDetector> toolDetectors)
    {
        ArgumentNullException.ThrowIfNull(toolDetectors);
        _toolDetectors = toolDetectors.ToArray();
    }

    public async Task<GitExecutableResolution> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var detector = _toolDetectors.FirstOrDefault(item =>
            string.Equals(item.ToolName, "Git", StringComparison.OrdinalIgnoreCase));

        if (detector is null)
        {
            return new GitExecutableResolution(
                false,
                null,
                null,
                "Git detector is not registered.");
        }

        var status = await detector.DetectAsync(cancellationToken).ConfigureAwait(false);
        if (!status.Installed || string.IsNullOrWhiteSpace(status.Path))
        {
            return new GitExecutableResolution(
                false,
                status.Path,
                status.Version,
                status.Message ?? "Git was not found on PATH.");
        }

        return new GitExecutableResolution(
            true,
            status.Path,
            status.Version,
            status.Message ?? "Git is ready.");
    }
}
