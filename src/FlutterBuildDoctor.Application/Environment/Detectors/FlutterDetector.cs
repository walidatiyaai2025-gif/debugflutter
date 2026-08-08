namespace FlutterBuildDoctor.Application.Environment.Detectors;

public sealed class FlutterDetector : IToolDetector
{
    public string ToolName => "Flutter";

    public Task<ToolDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ToolDetectionResult(
            ToolName,
            false,
            null,
            null,
            "Flutter detector ready for command execution integration."));
    }
}
