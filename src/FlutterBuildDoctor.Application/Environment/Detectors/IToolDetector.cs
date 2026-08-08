namespace FlutterBuildDoctor.Application.Environment.Detectors;

public interface IToolDetector
{
    string ToolName { get; }

    Task<ToolDetectionResult> DetectAsync(CancellationToken cancellationToken = default);
}
