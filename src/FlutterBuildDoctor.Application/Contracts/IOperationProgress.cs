namespace FlutterBuildDoctor.Application.Contracts;

public interface IOperationProgress
{
    void Report(OperationProgressUpdate update);
}

public sealed record OperationProgressUpdate(
    string Stage,
    string Message,
    int? Percent,
    DateTime Timestamp);
