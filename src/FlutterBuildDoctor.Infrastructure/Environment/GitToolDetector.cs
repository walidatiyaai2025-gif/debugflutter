using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Domain.Environment;
using FlutterBuildDoctor.Infrastructure.Tools;

namespace FlutterBuildDoctor.Infrastructure.Environment;

public sealed class GitToolDetector : ToolDetectorBase
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);
    private readonly IProcessRunner _processRunner;

    public GitToolDetector(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public override string ToolName => "Git";

    public override async Task<ToolStatus> DetectAsync(CancellationToken cancellationToken = default)
    {
        var locateResult = await _processRunner.RunAsync(
            new ProcessRequest(
                "where.exe",
                new[] { "git.exe" },
                Timeout: ProbeTimeout,
                DisplayName: "Locate Git"),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        var gitPath = locateResult.IsSuccess
            ? FirstOutputLine(locateResult)
            : null;

        if (string.IsNullOrWhiteSpace(gitPath))
        {
            return Missing(
                ToolName,
                locateResult.Status == ProcessExecutionStatus.TimedOut
                    ? "Git lookup timed out."
                    : "Git was not found on PATH.");
        }

        var versionResult = await _processRunner.RunAsync(
            new ProcessRequest(
                gitPath,
                new[] { "--version" },
                Timeout: ProbeTimeout,
                DisplayName: "Read Git version"),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (!versionResult.IsSuccess)
        {
            return new ToolStatus(
                ToolName,
                true,
                null,
                gitPath,
                versionResult.Status == ProcessExecutionStatus.TimedOut
                    ? "Git was found, but the version probe timed out."
                    : "Git was found, but the version probe failed.");
        }

        var version = ParseVersion(FirstOutputLine(versionResult));
        if (string.IsNullOrWhiteSpace(version))
        {
            return new ToolStatus(
                ToolName,
                true,
                null,
                gitPath,
                "Git was found, but its version output could not be parsed.");
        }

        return Ready(ToolName, version, gitPath);
    }

    private static string? FirstOutputLine(ProcessResult result)
        => result.Output
            .Where(line => line.Stream == ProcessStream.StdOut)
            .Select(line => line.Text.Trim())
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));

    private static string? ParseVersion(string? output)
    {
        const string prefix = "git version ";

        if (string.IsNullOrWhiteSpace(output) ||
            !output.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var version = output[prefix.Length..].Trim();
        return string.IsNullOrWhiteSpace(version) ? null : version;
    }
}
