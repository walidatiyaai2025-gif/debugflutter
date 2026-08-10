using System.Diagnostics;
using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Infrastructure.Processes;

public sealed class DetachedProcessLauncher : IDetachedProcessLauncher
{
    private readonly IProcessSecretRedactor _secretRedactor;

    public DetachedProcessLauncher(IProcessSecretRedactor? secretRedactor = null)
    {
        _secretRedactor = secretRedactor ?? new DefaultProcessSecretRedactor();
    }

    public ProcessLaunchResult Launch(ProcessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
        ArgumentNullException.ThrowIfNull(request.Arguments);
        var sanitizedCommand = _secretRedactor.SanitizeCommand(request);

        try
        {
            using var process = new Process
            {
                StartInfo = CreateStartInfo(request),
                EnableRaisingEvents = false
            };
            if (!process.Start())
                return new ProcessLaunchResult(false, null, sanitizedCommand, "Process failed to start.");

            return new ProcessLaunchResult(true, process.Id, sanitizedCommand);
        }
        catch (Exception ex)
        {
            return new ProcessLaunchResult(
                false,
                null,
                sanitizedCommand,
                _secretRedactor.RedactText(ex.Message, request));
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
            RedirectStandardOutput = false,
            RedirectStandardError = false,
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
}
