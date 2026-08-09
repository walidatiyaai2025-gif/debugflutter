namespace FlutterBuildDoctor.Application.Environment;

public interface IToolDetector
{
    string ToolName { get; }
    Task<Domain.Environment.ToolStatus> DetectAsync(CancellationToken cancellationToken = default);
}
