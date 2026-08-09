using FlutterBuildDoctor.Application.Environment;

namespace FlutterBuildDoctor.Flutter.Detection;

public sealed class DartSdkDetector : IDartSdkDetector
{
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
    private readonly IPathExecutableDiscovery _pathDiscovery;

    public DartSdkDetector(IPathExecutableDiscovery pathDiscovery)
    {
        _pathDiscovery = pathDiscovery ?? throw new ArgumentNullException(nameof(pathDiscovery));
    }

    public async Task<DartDetectionResult> DetectAsync(
        FlutterDetectionResult flutterResult,
        DartSdkDetectionRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(flutterResult);
        if (cancellationToken.IsCancellationRequested)
            return Cancelled(flutterResult.FlutterSdkPath);

        request ??= new DartSdkDetectionRequest();
        var pathResult = _pathDiscovery.Discover(new PathExecutableDiscoveryRequest(
            "dart",
            request.PathValue,
            request.PathExtValue));

        var candidates = new List<DartSdkCandidate>();
        if (pathResult.IsSuccess)
        {
            foreach (var match in pathResult.Matches)
            {
                candidates.Add(await BuildCandidateAsync(
                    match.FullPath,
                    isFlutterBundled: false,
                    match.IsPreferred,
                    match.IsShadowed,
                    cancellationToken).ConfigureAwait(false));
            }
        }

        if (cancellationToken.IsCancellationRequested)
            return Cancelled(flutterResult.FlutterSdkPath, candidates, pathResult);

        var flutterBundledPath = FindFlutterBundledDart(flutterResult);
        if (flutterBundledPath is not null)
        {
            var existingIndex = candidates.FindIndex(candidate => PathComparer.Equals(candidate.ExecutablePath, flutterBundledPath));
            if (existingIndex >= 0)
            {
                candidates[existingIndex] = candidates[existingIndex] with { IsFlutterBundled = true };
            }
            else
            {
                candidates.Add(await BuildCandidateAsync(
                    flutterBundledPath,
                    isFlutterBundled: true,
                    isPathPreferred: false,
                    isShadowed: false,
                    cancellationToken).ConfigureAwait(false));
            }
        }

        if (cancellationToken.IsCancellationRequested)
            return Cancelled(flutterResult.FlutterSdkPath, candidates, pathResult);

        var ordered = candidates
            .OrderByDescending(candidate => candidate.IsFlutterBundled)
            .ThenByDescending(candidate => candidate.IsPathPreferred)
            .ThenBy(candidate => candidate.ExecutablePath, PathComparer)
            .ToArray();
        var bundled = ordered.FirstOrDefault(candidate => candidate.IsFlutterBundled);
        var pathPreferred = ordered.FirstOrDefault(candidate => candidate.IsPathPreferred);
        var mismatch = bundled is not null && pathPreferred is not null &&
                       !PathComparer.Equals(bundled.ExecutablePath, pathPreferred.ExecutablePath);
        var usable = ordered.Where(candidate => candidate.IsUsable).ToArray();

        if (ordered.Length == 0)
        {
            return new DartDetectionResult(
                DartSdkDetectionStatus.Missing,
                flutterResult.FlutterSdkPath,
                bundled,
                pathPreferred,
                ordered,
                pathResult.HasConflict,
                mismatch,
                "Dart was not found in the detected Flutter SDK or on the effective Windows PATH.",
                pathResult);
        }

        if (usable.Length == 0)
        {
            var hasInvalidMetadata = ordered.Any(candidate => candidate.Message?.Contains("could not be read", StringComparison.OrdinalIgnoreCase) == true ||
                                                              candidate.Message?.Contains("empty", StringComparison.OrdinalIgnoreCase) == true);
            return new DartDetectionResult(
                hasInvalidMetadata ? DartSdkDetectionStatus.MetadataInvalid : DartSdkDetectionStatus.MetadataMissing,
                flutterResult.FlutterSdkPath,
                bundled,
                pathPreferred,
                ordered,
                pathResult.HasConflict,
                mismatch,
                "Dart executable candidates were found, but no usable version metadata could be resolved.",
                pathResult);
        }

        var warning = mismatch
            ? " The PATH-preferred Dart differs from Flutter's bundled Dart; both are preserved and no PATH changes were made."
            : pathResult.HasConflict
                ? " Multiple Dart executables are present on PATH and remain visible as conflict evidence."
                : string.Empty;
        var pathDiscoveryWarning = pathResult.IsSuccess
            ? string.Empty
            : $" PATH discovery reported: {pathResult.Message}";
        var primary = bundled?.IsUsable == true ? bundled : pathPreferred?.IsUsable == true ? pathPreferred : usable[0];

        return new DartDetectionResult(
            DartSdkDetectionStatus.Succeeded,
            flutterResult.FlutterSdkPath,
            bundled,
            pathPreferred,
            ordered,
            pathResult.HasConflict,
            mismatch,
            $"Dart {primary.Version} detected at '{primary.ExecutablePath}'.{warning}{pathDiscoveryWarning}",
            pathResult);
    }

    private static string? FindFlutterBundledDart(FlutterDetectionResult flutterResult)
    {
        if (!flutterResult.Installed || string.IsNullOrWhiteSpace(flutterResult.FlutterSdkPath))
            return null;

        var bin = Path.Combine(flutterResult.FlutterSdkPath, "bin", "cache", "dart-sdk", "bin");
        foreach (var fileName in new[] { "dart.exe", "dart.bat", "dart" })
        {
            var candidate = Path.Combine(bin, fileName);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        return null;
    }

    private static async Task<DartSdkCandidate> BuildCandidateAsync(
        string executablePath,
        bool isFlutterBundled,
        bool isPathPreferred,
        bool isShadowed,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(executablePath);
        string? sdkRoot = null;
        string? versionPath = null;
        string? raw = null;
        string? version = null;
        string? message = null;

        try
        {
            var executableDirectory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(executableDirectory) &&
                string.Equals(Path.GetFileName(executableDirectory), "bin", StringComparison.OrdinalIgnoreCase))
            {
                sdkRoot = Directory.GetParent(executableDirectory)?.FullName;
                if (!string.IsNullOrWhiteSpace(sdkRoot))
                {
                    versionPath = Path.Combine(sdkRoot, "version");
                    if (File.Exists(versionPath))
                    {
                        raw = await File.ReadAllTextAsync(versionPath, cancellationToken).ConfigureAwait(false);
                        version = raw.Trim();
                        if (version.Length == 0)
                        {
                            version = null;
                            message = "Dart version metadata file is empty.";
                        }
                    }
                    else
                    {
                        message = "Dart version metadata file is missing.";
                    }
                }
            }
            else
            {
                message = "Dart executable is not under an expected SDK bin directory.";
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            message = $"Dart version metadata could not be read: {ex.Message}";
        }

        return new DartSdkCandidate(
            fullPath,
            sdkRoot,
            version,
            isFlutterBundled,
            isPathPreferred,
            isShadowed,
            versionPath is not null && File.Exists(versionPath) ? versionPath : null,
            raw,
            message);
    }

    private static DartDetectionResult Cancelled(
        string? flutterSdkPath,
        IReadOnlyList<DartSdkCandidate>? candidates = null,
        PathExecutableDiscoveryResult? pathResult = null)
        => new(
            DartSdkDetectionStatus.Cancelled,
            flutterSdkPath,
            candidates?.FirstOrDefault(candidate => candidate.IsFlutterBundled),
            candidates?.FirstOrDefault(candidate => candidate.IsPathPreferred),
            candidates ?? Array.Empty<DartSdkCandidate>(),
            pathResult?.HasConflict ?? false,
            HasFlutterPathMismatch: false,
            Message: "Dart SDK detection was cancelled.",
            PathDiscovery: pathResult);
}
