using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Android.Detection;

public sealed class AndroidAdbDetector : IAndroidAdbDetector
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);
    private readonly IProcessRunner _processRunner;

    public AndroidAdbDetector(IProcessRunner processRunner)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public async Task<AndroidAdbDetectionResult> DetectAsync(
        AndroidSdkRootDetectionResult sdkRootResult,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sdkRootResult);

        var sdkRoot = sdkRootResult.EffectiveCandidate?.NormalizedPath ?? string.Empty;
        if (cancellationToken.IsCancellationRequested)
            return Cancelled(sdkRoot, null, null, null);

        if (!sdkRootResult.IsSuccess ||
            sdkRootResult.EffectiveCandidate is null ||
            !sdkRootResult.EffectiveCandidate.IsValid)
        {
            return new AndroidAdbDetectionResult(
                AndroidAdbDetectionStatus.AndroidSdkRootUnavailable,
                sdkRoot,
                PlatformToolsPath: null,
                AdbPath: null,
                AdbProtocolVersion: null,
                PlatformToolsVersion: null,
                InstalledAsPath: null,
                RawVersionOutput: null,
                RawSourceProperties: null,
                Message: "A validated effective Android SDK root is required before ADB can be detected.");
        }

        var platformToolsPath = Path.Combine(sdkRoot, "platform-tools");
        if (!Directory.Exists(platformToolsPath))
        {
            return new AndroidAdbDetectionResult(
                AndroidAdbDetectionStatus.PlatformToolsMissing,
                sdkRoot,
                platformToolsPath,
                AdbPath: null,
                AdbProtocolVersion: null,
                PlatformToolsVersion: null,
                InstalledAsPath: null,
                RawVersionOutput: null,
                RawSourceProperties: null,
                Message: $"Android platform-tools were not found under '{platformToolsPath}'.");
        }

        var adbPath = FindAdb(platformToolsPath);
        var rawSourceProperties = ReadSourceProperties(platformToolsPath);
        if (adbPath is null)
        {
            return new AndroidAdbDetectionResult(
                AndroidAdbDetectionStatus.AdbMissing,
                sdkRoot,
                platformToolsPath,
                AdbPath: null,
                AdbProtocolVersion: null,
                PlatformToolsVersion: ParseProperty(rawSourceProperties, "Pkg.Revision"),
                InstalledAsPath: null,
                RawVersionOutput: null,
                RawSourceProperties: rawSourceProperties,
                Message: $"platform-tools exists at '{platformToolsPath}', but adb.exe was not found.");
        }

        ProcessResult probeResult;
        try
        {
            probeResult = await _processRunner.RunAsync(
                new ProcessRequest(
                    adbPath,
                    new[] { "version" },
                    Timeout: ProbeTimeout,
                    DisplayName: "Read ADB version"),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Cancelled(sdkRoot, platformToolsPath, adbPath, rawSourceProperties);
        }

        var rawOutput = string.Join(
            System.Environment.NewLine,
            probeResult.Output.Select(line => line.Text));

        if (probeResult.Status == ProcessExecutionStatus.Cancelled)
            return Cancelled(sdkRoot, platformToolsPath, adbPath, rawSourceProperties, probeResult, rawOutput);

        if (probeResult.Status == ProcessExecutionStatus.TimedOut)
        {
            return new AndroidAdbDetectionResult(
                AndroidAdbDetectionStatus.TimedOut,
                sdkRoot,
                platformToolsPath,
                adbPath,
                AdbProtocolVersion: null,
                PlatformToolsVersion: ParseProperty(rawSourceProperties, "Pkg.Revision"),
                InstalledAsPath: null,
                RawVersionOutput: rawOutput,
                RawSourceProperties: rawSourceProperties,
                Message: "ADB was found, but the read-only version probe timed out.",
                ProbeResult: probeResult);
        }

        if (!probeResult.IsSuccess)
        {
            return new AndroidAdbDetectionResult(
                AndroidAdbDetectionStatus.ProbeFailed,
                sdkRoot,
                platformToolsPath,
                adbPath,
                AdbProtocolVersion: null,
                PlatformToolsVersion: ParseProperty(rawSourceProperties, "Pkg.Revision"),
                InstalledAsPath: null,
                RawVersionOutput: rawOutput,
                RawSourceProperties: rawSourceProperties,
                Message: "ADB was found, but the read-only version probe failed.",
                ProbeResult: probeResult);
        }

        var parsed = ParseVersionOutput(probeResult.Output);
        var packageRevision = parsed.PlatformToolsVersion ?? ParseProperty(rawSourceProperties, "Pkg.Revision");
        if (string.IsNullOrWhiteSpace(parsed.ProtocolVersion) || string.IsNullOrWhiteSpace(packageRevision))
        {
            return new AndroidAdbDetectionResult(
                AndroidAdbDetectionStatus.ParseFailed,
                sdkRoot,
                platformToolsPath,
                adbPath,
                parsed.ProtocolVersion,
                packageRevision,
                parsed.InstalledAsPath,
                rawOutput,
                rawSourceProperties,
                Message: "ADB version output completed successfully, but required version fields could not be parsed.",
                ProbeResult: probeResult);
        }

        return new AndroidAdbDetectionResult(
            AndroidAdbDetectionStatus.Succeeded,
            sdkRoot,
            platformToolsPath,
            adbPath,
            parsed.ProtocolVersion,
            packageRevision,
            parsed.InstalledAsPath,
            rawOutput,
            rawSourceProperties,
            Message: $"ADB {parsed.ProtocolVersion} / platform-tools {packageRevision} detected at '{adbPath}'.",
            ProbeResult: probeResult);
    }

    private static string? FindAdb(string platformToolsPath)
    {
        foreach (var fileName in new[] { "adb.exe", "adb" })
        {
            var candidate = Path.Combine(platformToolsPath, fileName);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        return null;
    }

    private static string? ReadSourceProperties(string platformToolsPath)
    {
        var path = Path.Combine(platformToolsPath, "source.properties");
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

    private static ParsedVersionOutput ParseVersionOutput(IReadOnlyList<ProcessOutputLine> output)
    {
        const string protocolPrefix = "Android Debug Bridge version ";
        const string platformPrefix = "Version ";
        const string installedPrefix = "Installed as ";
        string? protocol = null;
        string? platform = null;
        string? installedAs = null;

        foreach (var line in output.Select(item => item.Text.Trim()))
        {
            if (line.StartsWith(protocolPrefix, StringComparison.OrdinalIgnoreCase))
                protocol ??= ValueAfterPrefix(line, protocolPrefix);
            else if (line.StartsWith(platformPrefix, StringComparison.OrdinalIgnoreCase))
                platform ??= ValueAfterPrefix(line, platformPrefix);
            else if (line.StartsWith(installedPrefix, StringComparison.OrdinalIgnoreCase))
                installedAs ??= ValueAfterPrefix(line, installedPrefix);
        }

        return new ParsedVersionOutput(protocol, platform, installedAs);
    }

    private static string? ValueAfterPrefix(string value, string prefix)
    {
        var parsed = value[prefix.Length..].Trim();
        return parsed.Length == 0 ? null : parsed;
    }

    private static AndroidAdbDetectionResult Cancelled(
        string sdkRoot,
        string? platformToolsPath,
        string? adbPath,
        string? rawSourceProperties,
        ProcessResult? probeResult = null,
        string? rawOutput = null)
        => new(
            AndroidAdbDetectionStatus.Cancelled,
            sdkRoot,
            platformToolsPath,
            adbPath,
            AdbProtocolVersion: null,
            PlatformToolsVersion: ParseProperty(rawSourceProperties, "Pkg.Revision"),
            InstalledAsPath: null,
            RawVersionOutput: rawOutput,
            RawSourceProperties: rawSourceProperties,
            Message: "ADB detection was cancelled.",
            ProbeResult: probeResult);

    private sealed record ParsedVersionOutput(
        string? ProtocolVersion,
        string? PlatformToolsVersion,
        string? InstalledAsPath);
}
