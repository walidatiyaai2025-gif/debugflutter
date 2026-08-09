using System.Collections.Concurrent;
using System.Diagnostics;
using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Infrastructure.Processes;

public sealed class ProcessRunner : IProcessRunner
{
    private readonly IProcessSecretRedactor _secretRedactor;

    public ProcessRunner(IProcessSecretRedactor? secretRedactor = null)
    {
        _secretRedactor = secretRedactor ?? new DefaultProcessSecretRedactor();
    }

    public async Task<ProcessResult> RunAsync(
        ProcessRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
        ArgumentNullException.ThrowIfNull(request.Arguments);

        var executionId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;
        var sanitizedCommand = _secretRedactor.SanitizeCommand(request);
        var displayName = _secretRedactor.RedactText(
            string.IsNullOrWhiteSpace(request.DisplayName) ? request.FileName : request.DisplayName,
            request);
        var output = new ConcurrentQueue<ProcessOutputLine>();
        using var process = new Process { StartInfo = CreateStartInfo(request), EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) => Publish(e.Data, ProcessStream.StdOut);
        process.ErrorDataReceived += (_, e) => Publish(e.Data, ProcessStream.StdErr);

        try
        {
            if (!process.Start())
                return Finish(ProcessExecutionStatus.Failed, null, "Process failed to start.");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeoutCts = request.Timeout is { } timeout
                ? new CancellationTokenSource(timeout)
                : null;
            using var linkedCts = timeoutCts is null
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
                process.WaitForExit();
            }
            catch (OperationCanceledException)
            {
                TryKillTree(process);
                var timedOut = timeoutCts?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested;
                return Finish(
                    timedOut ? ProcessExecutionStatus.TimedOut : ProcessExecutionStatus.Cancelled,
                    null,
                    timedOut ? "Process timed out." : "Process was cancelled.");
            }

            return Finish(
                process.ExitCode == 0 ? ProcessExecutionStatus.Succeeded : ProcessExecutionStatus.Failed,
                process.ExitCode,
                process.ExitCode == 0 ? null : $"Process exited with code {process.ExitCode}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TryKillTree(process);
            return Finish(ProcessExecutionStatus.Failed, null, ex.Message);
        }

        void Publish(string? text, ProcessStream stream)
        {
            if (text is null)
                return;

            var line = new ProcessOutputLine(
                DateTimeOffset.UtcNow,
                stream,
                _secretRedactor.RedactText(text, request));
            output.Enqueue(line);
            progress?.Report(line);
        }

        ProcessResult Finish(ProcessExecutionStatus status, int? exitCode, string? failureReason)
        {
            var finishedAt = DateTimeOffset.UtcNow;
            var safeFailureReason = failureReason is null
                ? null
                : _secretRedactor.RedactText(failureReason, request);
            var receipt = new ProcessExecutionReceipt(
                executionId,
                displayName,
                sanitizedCommand,
                startedAt,
                finishedAt,
                status,
                exitCode,
                safeFailureReason);

            return new ProcessResult(
                status,
                exitCode,
                startedAt,
                finishedAt,
                output.ToArray(),
                sanitizedCommand,
                safeFailureReason,
                receipt);
        }
    }

    private static ProcessStartInfo CreateStartInfo(ProcessRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            WorkingDirectory = string.IsNullOrWhiteSpace(request.WorkingDirectory)
                ? System.Environment.CurrentDirectory
                : request.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in request.Arguments)
            startInfo.ArgumentList.Add(argument);

        if (request.Environment is not null)
        {
            foreach (var pair in request.Environment)
            {
                if (pair.Value is null)
                    startInfo.Environment.Remove(pair.Key);
                else
                    startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        return startInfo;
    }

    private static void TryKillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort; caller still receives a cancelled/failed receipt.
        }
    }
}
