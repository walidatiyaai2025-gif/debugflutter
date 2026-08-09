using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Flutter.Detection;

namespace FlutterBuildDoctor.Flutter.Doctor;

public enum FlutterDoctorExecutionStatus
{
    Succeeded = 0,
    FlutterUnavailable,
    InvalidRequest,
    Failed,
    Cancelled,
    TimedOut
}

public sealed record FlutterDoctorExecutionRequest(
    FlutterDetectionResult Flutter,
    TimeSpan? Timeout = null);

public sealed record FlutterDoctorExecutionResult(
    FlutterDoctorExecutionStatus Status,
    string? FlutterPath,
    string Message,
    ProcessResult? ProcessResult = null)
{
    public bool IsSuccess => Status == FlutterDoctorExecutionStatus.Succeeded;
}

public interface IFlutterDoctorExecutor
{
    Task<FlutterDoctorExecutionResult> ExecuteAsync(
        FlutterDoctorExecutionRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default);
}
