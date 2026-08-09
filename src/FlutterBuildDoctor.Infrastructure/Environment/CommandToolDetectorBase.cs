using FlutterBuildDoctor.Application.Environment;

namespace FlutterBuildDoctor.Infrastructure.Environment;

public abstract class CommandToolDetectorBase : IToolDetector
{
    public abstract string ToolName { get; }

    protected abstract string ExecutableName { get; }

    public abstract Task<Domain.Environment.ToolStatus> DetectAsync(CancellationToken cancellationToken = default);
}
