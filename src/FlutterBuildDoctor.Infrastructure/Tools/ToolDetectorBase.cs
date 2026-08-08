using FlutterBuildDoctor.Application.Environment;

namespace FlutterBuildDoctor.Infrastructure.Tools;

public abstract class ToolDetectorBase : IToolDetector
{
    public abstract string Name { get; }

    public abstract Task<ToolStatus> DetectAsync(CancellationToken cancellationToken = default);

    protected static ToolStatus Missing(string name, string message)
        => new(name, false, null, null, "Missing", message);

    protected static ToolStatus Ready(string name, string? version, string? path)
        => new(name, true, version, path, "Ready", null);
}
