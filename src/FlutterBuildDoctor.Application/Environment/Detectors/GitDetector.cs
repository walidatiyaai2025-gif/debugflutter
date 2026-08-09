namespace FlutterBuildDoctor.Application.Environment.Detectors;

public sealed class GitDetector : IToolDetector
{
    public string ToolName => "Git";

    public Task<ToolDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ToolDetectionResult(
            ToolName,
            false,
            null,
            null,
            "Git detector ready for command execution integration."));
    }
}
