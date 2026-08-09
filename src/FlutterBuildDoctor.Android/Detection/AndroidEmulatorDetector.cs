using System.Text.RegularExpressions;
using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Android.Detection;

public sealed partial class AndroidEmulatorDetector : IAndroidEmulatorDetector
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);
    private readonly IProcessRunner _processRunner;

    public AndroidEmulatorDetector(IProcessRunner processRunner)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public async Task<AndroidEmulatorDetectionResult> DetectAsync(
        AndroidSdkRootDetectionResult sdkRootResult,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sdkRootResult);

        var sdkRoot = sdkRootResult.EffectiveCandidate?.NormalizedPath ?? string.Empty;
        if (cancellationToken.IsCancellationRequested)
            return Cancelled(sdkRoot, null, null, null, null);

        if (!sdkRootResult.IsSuccess || sdkRootResult.EffectiveCandidate is null || !sdkRootResult.EffectiveCandidate.IsValid)
        {
            return new AndroidEmulatorDetectionResult(
                AndroidEmulatorDetectionStatus.AndroidSdkRootUnavailable,
                sdkRoot,
                EmulatorDirectory: null,
                EmulatorPath: null,
                Version: null,
                VersionSource: AndroidEmulatorVersionSource.None,
                RawVersionOutput: null,
                RawSourceProperties: null,
                Message: "A validated effective Android SDK root is required before the emulator can be detected.");
        }

        var emulatorDirectory = Path.Combine(sdkRoot, "emulator");
        if (!Directory.Exists(emulatorDirectory))
        {
            return new AndroidEmulatorDetectionResult(
                AndroidEmulatorDetectionStatus.EmulatorDirectoryMissing,
                sdkRoot,
                emulatorDirectory,
                EmulatorPath: null,
                Version: null,
                VersionSource: AndroidEmulatorVersionSource.None,
                RawVersionOutput: null,
                RawSourceProperties: null,
                Message: $"Android emulator package directory was not found at '{emulatorDirectory}'.");
        }

        var emulatorPath = FindEmulator(emulatorDirectory);
        var rawSourceProperties = ReadSourceProperties(emulatorDirectory);
        var packageRevision = ParseProperty(rawSourceProperties, "Pkg.Revision");
        if (emulatorPath is null)
        {
            return new AndroidEmulatorDetectionResult(
                AndroidEmulatorDetectionStatus.EmulatorMissing,
                sdkRoot,
                emulatorDirectory,
                EmulatorPath: null,
                Version: packageRevision,
                VersionSource: packageRevision is null ? AndroidEmulatorVersionSource.None : AndroidEmulatorVersionSource.SourceProperties,
                RawVersionOutput: null,
                RawSourceProperties: rawSourceProperties,
                Message: $"Android emulator package exists at '{emulatorDirectory}', but emulator.exe was not found.");
        }

        ProcessResult probeResult;
        try
        {
            probeResult = await _processRunner.RunAsync(
                new ProcessRequest(
                    emulatorPath,
                    new[] { "-version" },
                    Timeout: ProbeTimeout,
                    DisplayName: "Read Android emulator version"),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Cancelled(sdkRoot, emulatorDirectory, emulatorPath, rawSourceProperties, null);
        }

        var rawOutput = string.Join(
            System.Environment.NewLine,
            probeResult.Output.Select(line => line.Text));

        if (probeResult.Status == ProcessExecutionStatus.Cancelled)
            return Cancelled(sdkRoot, emulatorDirectory, emulatorPath, rawSourceProperties, probeResult, rawOutput);

        if (probeResult.Status == ProcessExecutionStatus.TimedOut)
        {
            return new AndroidEmulatorDetectionResult(
                AndroidEmulatorDetectionStatus.TimedOut,
                sdkRoot,
                emulatorDirectory,
                emulatorPath,
                Version: packageRevision,
                VersionSource: packageRevision is null ? AndroidEmulatorVersionSource.None : AndroidEmulatorVersionSource.SourceProperties,
                RawVersionOutput: rawOutput,
                RawSourceProperties: rawSourceProperties,
                Message: "Android emulator was found, but the read-only version probe timed out.",
                ProbeResult: probeResult);
        }

        if (!probeResult.IsSuccess)
        {
            return new AndroidEmulatorDetectionResult(
                AndroidEmulatorDetectionStatus.ProbeFailed,
                sdkRoot,
                emulatorDirectory,
                emulatorPath,
                Version: packageRevision,
                VersionSource: packageRevision is null ? AndroidEmulatorVersionSource.None : AndroidEmulatorVersionSource.SourceProperties,
                RawVersionOutput: rawOutput,
                RawSourceProperties: rawSourceProperties,
                Message: "Android emulator was found, but the read-only version probe failed.",
                ProbeResult: probeResult);
        }

        var commandVersion = ParseCommandVersion(probeResult.Output);
        if (!string.IsNullOrWhiteSpace(commandVersion))
        {
            var mismatchSuffix = !string.IsNullOrWhiteSpace(packageRevision) &&
                                 !string.Equals(commandVersion, packageRevision, StringComparison.OrdinalIgnoreCase)
                ? $" Package metadata reports revision {packageRevision}; both values are preserved as evidence."
                : string.Empty;

            return new AndroidEmulatorDetectionResult(
                AndroidEmulatorDetectionStatus.Succeeded,
                sdkRoot,
                emulatorDirectory,
                emulatorPath,
                commandVersion,
                AndroidEmulatorVersionSource.CommandOutput,
                rawOutput,
                rawSourceProperties,
                $"Android emulator {commandVersion} detected at '{emulatorPath}'.{mismatchSuffix}",
                ProbeResult: probeResult);
        }

        if (!string.IsNullOrWhiteSpace(packageRevision))
        {
            return new AndroidEmulatorDetectionResult(
                AndroidEmulatorDetectionStatus.Succeeded,
                sdkRoot,
                emulatorDirectory,
                emulatorPath,
                packageRevision,
                AndroidEmulatorVersionSource.SourceProperties,
                rawOutput,
                rawSourceProperties,
                $"Android emulator detected at '{emulatorPath}'. Command output did not expose a parseable version; package revision {packageRevision} is reported from source.properties.",
                ProbeResult: probeResult);
        }

        return new AndroidEmulatorDetectionResult(
            AndroidEmulatorDetectionStatus.VersionUnavailable,
            sdkRoot,
            emulatorDirectory,
            emulatorPath,
            Version: null,
            VersionSource: AndroidEmulatorVersionSource.None,
            RawVersionOutput: rawOutput,
            RawSourceProperties: rawSourceProperties,
            Message: "Android emulator version output completed successfully, but no version could be parsed and package metadata did not provide one.",
            ProbeResult: probeResult);
    }

    private static string? FindEmulator(string directory)
    {
        foreach (var fileName in new[] { "emulator.exe", "emulator" })
        {
            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        return null;
    }

    private static string? ReadSourceProperties(string directory)
    {
        var path = Path.Combine(directory, "source.properties");
        if (!File.Exists(path))
            return null;

        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"[unavailable] {ex.Message}";
        }
    }

    private static string? ParseProperty(string? raw, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("[unavailable]", StringComparison.Ordinal))
            return null;

        foreach (var line in raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var trimmed = line.Trim();
            var separator = trimmed.IndexOf('=');
            if (separator <= 0)
                continue;

            if (!string.Equals(trimmed[..separator].Trim(), propertyName, StringComparison.OrdinalIgnoreCase))
                continue;

            var value = trimmed[(separator + 1)..].Trim();
            return value.Length == 0 ? null : value;
        }

        return null;
    }

    private static string? ParseCommandVersion(IReadOnlyList<ProcessOutputLine> output)
    {
        foreach (var line in output.Select(item => item.Text.Trim()))
        {
            var match = EmulatorVersionRegex().Match(line);
            if (match.Success)
                return match.Groups[1].Value;
        }

        return null;
    }

    private static AndroidEmulatorDetectionResult Cancelled(
        string sdkRoot,
        string? emulatorDirectory,
        string? emulatorPath,
        string? rawSourceProperties,
        ProcessResult? probeResult,
        string? rawOutput = null)
        => new(
            AndroidEmulatorDetectionStatus.Cancelled,
            sdkRoot,
            emulatorDirectory,
            emulatorPath,
            Version: null,
            VersionSource: AndroidEmulatorVersionSource.None,
            RawVersionOutput: rawOutput,
            RawSourceProperties: rawSourceProperties,
            Message: "Android emulator detection was cancelled.",
            ProbeResult: probeResult);

    [GeneratedRegex(@"Android\s+emulator\s+version\s+([^\s]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmulatorVersionRegex();
}
