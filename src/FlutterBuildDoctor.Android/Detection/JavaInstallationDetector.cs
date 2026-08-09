using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Android.Detection;

public sealed class JavaInstallationDetector : IJavaInstallationDetector
{
    private static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromSeconds(8);
    private readonly IPathExecutableDiscovery _pathDiscovery;
    private readonly IProcessRunner _processRunner;

    public JavaInstallationDetector(
        IPathExecutableDiscovery pathDiscovery,
        IProcessRunner processRunner)
    {
        _pathDiscovery = pathDiscovery ?? throw new ArgumentNullException(nameof(pathDiscovery));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public async Task<JavaDetectionResult> DetectAsync(
        JavaDetectionRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        request ??= new JavaDetectionRequest();

        if (cancellationToken.IsCancellationRequested)
        {
            return Cancelled(EmptyDiscovery());
        }

        var pathResult = _pathDiscovery.Discover(new PathExecutableDiscoveryRequest(
            "java",
            request.PathValue,
            request.PathExtValue));

        if (!pathResult.IsSuccess)
        {
            return new JavaDetectionResult(
                JavaDetectionStatus.MetadataInvalid,
                PreferredInstallation: null,
                Installations: Array.Empty<JavaInstallation>(),
                HasConflict: false,
                PathDiscovery: pathResult,
                Message: pathResult.Message ?? "Java PATH discovery failed.");
        }

        if (!pathResult.IsFound)
        {
            return new JavaDetectionResult(
                JavaDetectionStatus.Missing,
                PreferredInstallation: null,
                Installations: Array.Empty<JavaInstallation>(),
                HasConflict: false,
                PathDiscovery: pathResult,
                Message: "Java was not found on the effective Windows PATH.");
        }

        var installations = new List<JavaInstallation>(pathResult.Matches.Count);
        var timeout = request.ProbeTimeout ?? DefaultProbeTimeout;

        foreach (var match in pathResult.Matches)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Cancelled(pathResult, installations);
            }

            var installation = await ProbeAsync(
                match,
                timeout,
                cancellationToken).ConfigureAwait(false);
            installations.Add(installation);

            if (installation.ProbeResult?.Status == ProcessExecutionStatus.Cancelled)
            {
                return Cancelled(pathResult, installations);
            }
        }

        var preferred = installations.FirstOrDefault(static installation => installation.IsPreferred)
            ?? installations.FirstOrDefault();

        if (preferred is null)
        {
            return new JavaDetectionResult(
                JavaDetectionStatus.Missing,
                PreferredInstallation: null,
                Installations: installations,
                HasConflict: pathResult.HasConflict,
                PathDiscovery: pathResult,
                Message: "Java PATH entries were found, but no installation could be selected.");
        }

        var preferredProbeStatus = preferred.ProbeResult?.Status;
        if (preferredProbeStatus == ProcessExecutionStatus.TimedOut)
        {
            return new JavaDetectionResult(
                JavaDetectionStatus.TimedOut,
                preferred,
                installations,
                pathResult.HasConflict,
                pathResult,
                $"The preferred Java installation at '{preferred.ExecutablePath}' timed out during version discovery.");
        }

        if (preferredProbeStatus == ProcessExecutionStatus.Cancelled)
        {
            return Cancelled(pathResult, installations, preferred);
        }

        if (preferredProbeStatus != ProcessExecutionStatus.Succeeded ||
            string.IsNullOrWhiteSpace(preferred.Version))
        {
            return new JavaDetectionResult(
                JavaDetectionStatus.ProbeFailed,
                preferred,
                installations,
                pathResult.HasConflict,
                pathResult,
                $"Java was found at '{preferred.ExecutablePath}', but its version/vendor metadata could not be read.");
        }

        var conflictSuffix = pathResult.HasConflict
            ? $" {pathResult.Matches.Count - 1} additional Java executable match(es) are shadowed by PATH order."
            : string.Empty;
        var jdkLabel = preferred.IsJdk ? "JDK" : "Java runtime";

        return new JavaDetectionResult(
            JavaDetectionStatus.Succeeded,
            preferred,
            installations,
            pathResult.HasConflict,
            pathResult,
            $"{jdkLabel} {preferred.Version} ({preferred.Vendor ?? "unknown vendor"}) detected at '{preferred.ExecutablePath}'.{conflictSuffix}");
    }

    private async Task<JavaInstallation> ProbeAsync(
        PathExecutableMatch match,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(
            new ProcessRequest(
                match.FullPath,
                new[] { "-XshowSettings:properties", "-version" },
                Timeout: timeout,
                DisplayName: $"Read Java metadata: {match.FullPath}"),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var metadata = ParseProperties(result);
        var javaHome = metadata.GetValueOrDefault("java.home") ?? DeriveJavaHome(match.FullPath);
        var version = metadata.GetValueOrDefault("java.version") ?? ParseVersionBanner(result);
        var vendor = metadata.GetValueOrDefault("java.vendor");
        var architecture = metadata.GetValueOrDefault("os.arch");
        var javacPath = FindJavac(javaHome);
        var isJdk = javacPath is not null;

        var message = result.Status switch
        {
            ProcessExecutionStatus.Succeeded when !string.IsNullOrWhiteSpace(version) => null,
            ProcessExecutionStatus.Succeeded => "Java exited successfully, but java.version could not be parsed.",
            ProcessExecutionStatus.TimedOut => "Java metadata probe timed out.",
            ProcessExecutionStatus.Cancelled => "Java metadata probe was cancelled.",
            _ => result.FailureReason ?? "Java metadata probe failed."
        };

        return new JavaInstallation(
            match.FullPath,
            javaHome,
            version,
            vendor,
            architecture,
            isJdk,
            javacPath,
            match.PathIndex,
            match.ResolutionOrder,
            match.IsPreferred,
            match.IsShadowed,
            message,
            result);
    }

    private static Dictionary<string, string> ParseProperties(ProcessResult result)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var outputLine in result.Output)
        {
            var line = outputLine.Text.Trim();
            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex == line.Length - 1)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            if (key.Length == 0 || value.Length == 0)
            {
                continue;
            }

            properties[key] = value;
        }

        return properties;
    }

    private static string? ParseVersionBanner(ProcessResult result)
    {
        foreach (var outputLine in result.Output)
        {
            var line = outputLine.Text.Trim();
            var markerIndex = line.IndexOf("version \"", StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                continue;
            }

            var valueStart = markerIndex + "version \"".Length;
            var valueEnd = line.IndexOf('"', valueStart);
            if (valueEnd > valueStart)
            {
                return line[valueStart..valueEnd].Trim();
            }
        }

        return null;
    }

    private static string? DeriveJavaHome(string executablePath)
    {
        try
        {
            var binDirectory = Path.GetDirectoryName(executablePath);
            if (string.IsNullOrWhiteSpace(binDirectory) ||
                !string.Equals(
                    Path.GetFileName(binDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                    "bin",
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return Directory.GetParent(binDirectory)?.FullName;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string? FindJavac(string? javaHome)
    {
        if (string.IsNullOrWhiteSpace(javaHome))
        {
            return null;
        }

        try
        {
            var direct = Path.Combine(javaHome, "bin", "javac.exe");
            if (File.Exists(direct))
            {
                return direct;
            }

            var trimmed = javaHome.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(Path.GetFileName(trimmed), "jre", StringComparison.OrdinalIgnoreCase))
            {
                var parent = Directory.GetParent(trimmed);
                if (parent is not null)
                {
                    var legacyJdk = Path.Combine(parent.FullName, "bin", "javac.exe");
                    if (File.Exists(legacyJdk))
                    {
                        return legacyJdk;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        return null;
    }

    private static JavaDetectionResult Cancelled(
        PathExecutableDiscoveryResult pathResult,
        IReadOnlyList<JavaInstallation>? installations = null,
        JavaInstallation? preferred = null)
        => new(
            JavaDetectionStatus.Cancelled,
            preferred,
            installations ?? Array.Empty<JavaInstallation>(),
            pathResult.HasConflict,
            pathResult,
            "Java installation detection was cancelled.");

    private static PathExecutableDiscoveryResult EmptyDiscovery()
        => new(
            PathExecutableDiscoveryStatus.Succeeded,
            "java",
            Array.Empty<PathExecutableMatch>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<IgnoredPathEntry>());
}
