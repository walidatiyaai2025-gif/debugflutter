using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Android.Detection;

public sealed class AndroidLicenseDetector : IAndroidLicenseDetector
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);
    private readonly IProcessRunner _processRunner;

    public AndroidLicenseDetector(IProcessRunner processRunner)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public async Task<AndroidLicenseDetectionResult> DetectAsync(
        AndroidCommandLineToolsDetectionResult commandLineToolsResult,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commandLineToolsResult);

        var sdkRoot = commandLineToolsResult.AndroidSdkRoot;
        var licenseFiles = EnumerateLicenseFiles(sdkRoot);
        var effective = commandLineToolsResult.EffectiveCandidate;
        var sdkManagerPath = effective?.SdkManagerPath;

        if (cancellationToken.IsCancellationRequested)
            return Cancelled(sdkRoot, sdkManagerPath, effective?.Revision, licenseFiles);

        if (effective is null ||
            !effective.SdkManagerExists ||
            string.IsNullOrWhiteSpace(sdkManagerPath) ||
            !File.Exists(sdkManagerPath))
        {
            return new AndroidLicenseDetectionResult(
                AndroidLicenseDetectionStatus.SdkManagerUnavailable,
                sdkRoot,
                sdkManagerPath,
                effective?.Revision,
                licenseFiles,
                RawOutput: null,
                Message: "A usable effective sdkmanager installation is required before Android license status can be probed.");
        }

        var request = BuildProbeRequest(sdkManagerPath);
        ProcessResult processResult;
        try
        {
            processResult = await _processRunner.RunAsync(
                request,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Cancelled(sdkRoot, sdkManagerPath, effective.Revision, licenseFiles);
        }

        var rawOutput = string.Join(
            System.Environment.NewLine,
            processResult.Output.Select(line => line.Text));
        var evidence = ClassifyOutput(rawOutput);

        if (processResult.Status == ProcessExecutionStatus.Cancelled)
            return Cancelled(sdkRoot, sdkManagerPath, effective.Revision, licenseFiles, processResult, rawOutput);

        if (evidence == LicenseEvidence.Accepted)
        {
            return new AndroidLicenseDetectionResult(
                AndroidLicenseDetectionStatus.Accepted,
                sdkRoot,
                sdkManagerPath,
                effective.Revision,
                licenseFiles,
                rawOutput,
                "All Android SDK package licenses are reported as accepted.",
                processResult);
        }

        if (evidence == LicenseEvidence.Pending)
        {
            var boundedSuffix = processResult.Status == ProcessExecutionStatus.TimedOut
                ? " The bounded probe was stopped without providing any acceptance input."
                : string.Empty;
            return new AndroidLicenseDetectionResult(
                AndroidLicenseDetectionStatus.Pending,
                sdkRoot,
                sdkManagerPath,
                effective.Revision,
                licenseFiles,
                rawOutput,
                $"One or more Android SDK package licenses require review/acceptance.{boundedSuffix}",
                processResult);
        }

        if (processResult.Status == ProcessExecutionStatus.TimedOut)
        {
            return new AndroidLicenseDetectionResult(
                AndroidLicenseDetectionStatus.TimedOut,
                sdkRoot,
                sdkManagerPath,
                effective.Revision,
                licenseFiles,
                rawOutput,
                "Android license status probe timed out before producing decisive evidence. No acceptance input was provided.",
                processResult);
        }

        if (!processResult.IsSuccess)
        {
            return new AndroidLicenseDetectionResult(
                AndroidLicenseDetectionStatus.ProbeFailed,
                sdkRoot,
                sdkManagerPath,
                effective.Revision,
                licenseFiles,
                rawOutput,
                "Android license status probe failed before producing decisive accepted/pending evidence.",
                processResult);
        }

        return new AndroidLicenseDetectionResult(
            AndroidLicenseDetectionStatus.Indeterminate,
            sdkRoot,
            sdkManagerPath,
            effective.Revision,
            licenseFiles,
            rawOutput,
            "sdkmanager completed, but its output did not contain a recognized Android license status.",
            processResult);
    }

    private static ProcessRequest BuildProbeRequest(string sdkManagerPath)
    {
        var command = $"call \"{sdkManagerPath}\" --licenses < NUL";
        return new ProcessRequest(
            "cmd.exe",
            new[] { "/d", "/v:off", "/s", "/c", command },
            Timeout: ProbeTimeout,
            DisplayName: "Read Android license status");
    }

    private static IReadOnlyList<string> EnumerateLicenseFiles(string sdkRoot)
    {
        if (string.IsNullOrWhiteSpace(sdkRoot))
            return Array.Empty<string>();

        var directory = Path.Combine(sdkRoot, "licenses");
        if (!Directory.Exists(directory))
            return Array.Empty<string>();

        try
        {
            return Directory
                .EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new[] { $"[unavailable] {ex.Message}" };
        }
    }

    private static LicenseEvidence ClassifyOutput(string? rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
            return LicenseEvidence.None;

        if (ContainsAny(
                rawOutput,
                "All SDK package licenses accepted",
                "All SDK package licenses accepted."))
        {
            return LicenseEvidence.Accepted;
        }

        if (ContainsAny(
                rawOutput,
                "licenses have not been accepted",
                "licenses not accepted",
                "Review licenses that have not been accepted",
                "Accept? (y/N)",
                "Accept? [y/N]",
                "Accept? (y/n)",
                "Accept? [y/n]"))
        {
            return LicenseEvidence.Pending;
        }

        return LicenseEvidence.None;
    }

    private static bool ContainsAny(string value, params string[] patterns)
        => patterns.Any(pattern => value.Contains(pattern, StringComparison.OrdinalIgnoreCase));

    private static AndroidLicenseDetectionResult Cancelled(
        string sdkRoot,
        string? sdkManagerPath,
        string? revision,
        IReadOnlyList<string> licenseFiles,
        ProcessResult? processResult = null,
        string? rawOutput = null)
        => new(
            AndroidLicenseDetectionStatus.Cancelled,
            sdkRoot,
            sdkManagerPath,
            revision,
            licenseFiles,
            rawOutput,
            "Android license status detection was cancelled. No acceptance input was provided.",
            processResult);

    private enum LicenseEvidence
    {
        None = 0,
        Accepted,
        Pending
    }
}
