using System.IO;
using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Flutter.Doctor;

public sealed class FlutterDoctorExecutor : IFlutterDoctorExecutor
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);
    private readonly IProcessRunner _processRunner;

    public FlutterDoctorExecutor(IProcessRunner processRunner)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public async Task<FlutterDoctorExecutionResult> ExecuteAsync(
        FlutterDoctorExecutionRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Flutter);

        var flutterPath = request.Flutter.FlutterPath;
        if (!request.Flutter.Installed || string.IsNullOrWhiteSpace(flutterPath))
        {
            return new FlutterDoctorExecutionResult(
                FlutterDoctorExecutionStatus.FlutterUnavailable,
                flutterPath,
                "A detected Flutter executable is required before flutter doctor can run.");
        }

        var timeout = request.Timeout ?? DefaultTimeout;
        if (timeout <= TimeSpan.Zero)
        {
            return new FlutterDoctorExecutionResult(
                FlutterDoctorExecutionStatus.InvalidRequest,
                flutterPath,
                "Flutter doctor timeout must be greater than zero.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new FlutterDoctorExecutionResult(
                FlutterDoctorExecutionStatus.Cancelled,
                flutterPath,
                "Flutter doctor execution was cancelled before it started.");
        }

        ProcessResult processResult;
        try
        {
            processResult = await _processRunner.RunAsync(
                BuildProcessRequest(flutterPath, timeout),
                progress,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new FlutterDoctorExecutionResult(
                FlutterDoctorExecutionStatus.Cancelled,
                flutterPath,
                "Flutter doctor execution was cancelled.");
        }
        catch (Exception ex)
        {
            return new FlutterDoctorExecutionResult(
                FlutterDoctorExecutionStatus.Failed,
                flutterPath,
                $"Flutter doctor could not be started: {ex.Message}");
        }

        return processResult.Status switch
        {
            ProcessExecutionStatus.Succeeded => new FlutterDoctorExecutionResult(
                FlutterDoctorExecutionStatus.Succeeded,
                flutterPath,
                "flutter doctor -v completed successfully.",
                processResult),
            ProcessExecutionStatus.Cancelled => new FlutterDoctorExecutionResult(
                FlutterDoctorExecutionStatus.Cancelled,
                flutterPath,
                "Flutter doctor execution was cancelled.",
                processResult),
            ProcessExecutionStatus.TimedOut => new FlutterDoctorExecutionResult(
                FlutterDoctorExecutionStatus.TimedOut,
                flutterPath,
                "Flutter doctor execution timed out.",
                processResult),
            _ => new FlutterDoctorExecutionResult(
                FlutterDoctorExecutionStatus.Failed,
                flutterPath,
                "flutter doctor -v failed. Raw process evidence was preserved.",
                processResult)
        };
    }

    private static ProcessRequest BuildProcessRequest(string flutterPath, TimeSpan timeout)
    {
        var extension = Path.GetExtension(flutterPath);
        if (extension.Equals(".bat", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            var command = $"call \"{flutterPath}\" doctor -v";
            return new ProcessRequest(
                "cmd.exe",
                new[] { "/d", "/v:off", "/s", "/c", command },
                Timeout: timeout,
                DisplayName: "Flutter doctor -v");
        }

        return new ProcessRequest(
            flutterPath,
            new[] { "doctor", "-v" },
            Timeout: timeout,
            DisplayName: "Flutter doctor -v");
    }
}
