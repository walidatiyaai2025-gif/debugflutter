using System.Text.Json;
using FlutterBuildDoctor.Application.Environment;

namespace FlutterBuildDoctor.Flutter.Detection;

public sealed class FlutterSdkDetector : IFlutterSdkDetector
{
    private readonly IPathExecutableDiscovery _pathDiscovery;

    public FlutterSdkDetector(IPathExecutableDiscovery pathDiscovery)
    {
        _pathDiscovery = pathDiscovery ?? throw new ArgumentNullException(nameof(pathDiscovery));
    }

    public async Task<FlutterDetectionResult> DetectAsync(
        FlutterSdkDetectionRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Cancelled();
        }

        request ??= new FlutterSdkDetectionRequest();
        var pathResult = _pathDiscovery.Discover(new PathExecutableDiscoveryRequest(
            "flutter",
            request.PathValue,
            request.PathExtValue));

        if (!pathResult.IsSuccess)
        {
            return new FlutterDetectionResult(
                FlutterSdkDetectionStatus.MetadataInvalid,
                Installed: false,
                FlutterPath: null,
                FlutterSdkPath: null,
                FlutterVersion: null,
                Channel: null,
                Candidates: Array.Empty<FlutterSdkCandidate>(),
                HasConflict: false,
                Message: pathResult.Message ?? "Flutter PATH discovery failed.",
                PathDiscovery: pathResult);
        }

        var candidates = pathResult.Matches
            .Select(BuildCandidate)
            .ToArray();

        if (candidates.Length == 0 || pathResult.PreferredMatch is null)
        {
            return new FlutterDetectionResult(
                FlutterSdkDetectionStatus.Missing,
                Installed: false,
                FlutterPath: null,
                FlutterSdkPath: null,
                FlutterVersion: null,
                Channel: null,
                Candidates: candidates,
                HasConflict: pathResult.HasConflict,
                Message: "Flutter was not found on the effective Windows PATH.",
                PathDiscovery: pathResult);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var preferred = candidates.First(static candidate => candidate.IsPreferred);
        if (!preferred.HasExpectedSdkLayout || string.IsNullOrWhiteSpace(preferred.SdkRoot))
        {
            return new FlutterDetectionResult(
                FlutterSdkDetectionStatus.InvalidSdkLayout,
                Installed: true,
                FlutterPath: preferred.ExecutablePath,
                FlutterSdkPath: preferred.SdkRoot,
                FlutterVersion: null,
                Channel: null,
                Candidates: candidates,
                HasConflict: pathResult.HasConflict,
                Message: $"PATH resolves Flutter to '{preferred.ExecutablePath}', but that executable is not under an expected Flutter SDK 'bin' directory.",
                PathDiscovery: pathResult);
        }

        try
        {
            var cached = await TryReadCachedVersionAsync(preferred.SdkRoot, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                return Success(preferred, candidates, pathResult, cached);
            }

            var legacy = await TryReadLegacyVersionAsync(preferred.SdkRoot, cancellationToken).ConfigureAwait(false);
            if (legacy is not null)
            {
                return Success(preferred, candidates, pathResult, legacy);
            }

            return new FlutterDetectionResult(
                FlutterSdkDetectionStatus.MetadataMissing,
                Installed: true,
                FlutterPath: preferred.ExecutablePath,
                FlutterSdkPath: preferred.SdkRoot,
                FlutterVersion: null,
                Channel: null,
                Candidates: candidates,
                HasConflict: pathResult.HasConflict,
                Message: $"Flutter was found at '{preferred.ExecutablePath}', but version/channel metadata was not available under '{preferred.SdkRoot}'.",
                PathDiscovery: pathResult);
        }
        catch (OperationCanceledException)
        {
            return Cancelled(candidates, pathResult, preferred);
        }
        catch (JsonException ex)
        {
            var metadataPath = Path.Combine(preferred.SdkRoot, "bin", "cache", "flutter.version.json");
            string? raw = null;
            try
            {
                if (File.Exists(metadataPath))
                {
                    raw = await File.ReadAllTextAsync(metadataPath, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (IOException)
            {
                // Raw evidence is best effort; the primary parse error is preserved below.
            }
            catch (UnauthorizedAccessException)
            {
                // Raw evidence is best effort; the primary parse error is preserved below.
            }

            return new FlutterDetectionResult(
                FlutterSdkDetectionStatus.MetadataInvalid,
                Installed: true,
                FlutterPath: preferred.ExecutablePath,
                FlutterSdkPath: preferred.SdkRoot,
                FlutterVersion: null,
                Channel: null,
                Candidates: candidates,
                HasConflict: pathResult.HasConflict,
                MetadataSource: FlutterVersionMetadataSource.CachedVersionJson,
                Message: $"Flutter version metadata could not be parsed: {ex.Message}",
                RawMetadata: raw,
                PathDiscovery: pathResult);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new FlutterDetectionResult(
                FlutterSdkDetectionStatus.MetadataInvalid,
                Installed: true,
                FlutterPath: preferred.ExecutablePath,
                FlutterSdkPath: preferred.SdkRoot,
                FlutterVersion: null,
                Channel: null,
                Candidates: candidates,
                HasConflict: pathResult.HasConflict,
                Message: $"Flutter metadata could not be read: {ex.Message}",
                PathDiscovery: pathResult);
        }
    }

    private static FlutterDetectionResult Success(
        FlutterSdkCandidate preferred,
        IReadOnlyList<FlutterSdkCandidate> candidates,
        PathExecutableDiscoveryResult pathResult,
        VersionMetadata metadata)
    {
        var conflictSuffix = pathResult.HasConflict
            ? $" {pathResult.Matches.Count - 1} additional Flutter executable match(es) are shadowed by PATH order."
            : string.Empty;

        return new FlutterDetectionResult(
            FlutterSdkDetectionStatus.Succeeded,
            Installed: true,
            FlutterPath: preferred.ExecutablePath,
            FlutterSdkPath: preferred.SdkRoot,
            FlutterVersion: metadata.Version,
            Channel: metadata.Channel,
            Candidates: candidates,
            HasConflict: pathResult.HasConflict,
            MetadataSource: metadata.Source,
            Message: $"Flutter {metadata.Version} ({metadata.Channel}) detected at '{preferred.ExecutablePath}'.{conflictSuffix}",
            RawMetadata: metadata.Raw,
            PathDiscovery: pathResult);
    }

    private static FlutterSdkCandidate BuildCandidate(PathExecutableMatch match)
    {
        string? sdkRoot = null;
        var expectedLayout = false;
        string? metadataPath = null;

        try
        {
            var executableDirectory = Path.GetDirectoryName(match.FullPath);
            if (!string.IsNullOrWhiteSpace(executableDirectory) &&
                string.Equals(
                    Path.GetFileName(executableDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                    "bin",
                    StringComparison.OrdinalIgnoreCase))
            {
                var parent = Directory.GetParent(executableDirectory);
                if (parent is not null)
                {
                    sdkRoot = parent.FullName;
                    expectedLayout = Directory.Exists(executableDirectory);
                    metadataPath = Path.Combine(sdkRoot, "bin", "cache", "flutter.version.json");
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            sdkRoot = null;
            expectedLayout = false;
            metadataPath = null;
        }

        return new FlutterSdkCandidate(
            match.FullPath,
            sdkRoot,
            match.PathIndex,
            match.ResolutionOrder,
            match.IsPreferred,
            match.IsShadowed,
            expectedLayout,
            metadataPath);
    }

    private static async Task<VersionMetadata?> TryReadCachedVersionAsync(
        string sdkRoot,
        CancellationToken cancellationToken)
    {
        var metadataPath = Path.Combine(sdkRoot, "bin", "cache", "flutter.version.json");
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        var raw = await File.ReadAllTextAsync(metadataPath, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement;

        var version = GetRequiredString(root, "frameworkVersion");
        var channel = GetRequiredString(root, "channel");

        return new VersionMetadata(
            version,
            channel,
            FlutterVersionMetadataSource.CachedVersionJson,
            raw);
    }

    private static async Task<VersionMetadata?> TryReadLegacyVersionAsync(
        string sdkRoot,
        CancellationToken cancellationToken)
    {
        var versionPath = Path.Combine(sdkRoot, "version");
        var headPath = Path.Combine(sdkRoot, ".git", "HEAD");
        if (!File.Exists(versionPath) || !File.Exists(headPath))
        {
            return null;
        }

        var version = (await File.ReadAllTextAsync(versionPath, cancellationToken).ConfigureAwait(false)).Trim();
        var head = (await File.ReadAllTextAsync(headPath, cancellationToken).ConfigureAwait(false)).Trim();
        const string branchPrefix = "ref: refs/heads/";

        if (version.Length == 0 || !head.StartsWith(branchPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var channel = head[branchPrefix.Length..].Trim();
        if (channel.Length == 0)
        {
            return null;
        }

        return new VersionMetadata(
            version,
            channel,
            FlutterVersionMetadataSource.LegacyVersionAndGitHead,
            $"version={version}\nHEAD={head}");
    }

    private static string GetRequiredString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new JsonException($"Required property '{propertyName}' is missing or empty.");
        }

        return property.GetString()!.Trim();
    }

    private static FlutterDetectionResult Cancelled(
        IReadOnlyList<FlutterSdkCandidate>? candidates = null,
        PathExecutableDiscoveryResult? pathResult = null,
        FlutterSdkCandidate? preferred = null)
        => new(
            FlutterSdkDetectionStatus.Cancelled,
            Installed: preferred is not null,
            FlutterPath: preferred?.ExecutablePath,
            FlutterSdkPath: preferred?.SdkRoot,
            FlutterVersion: null,
            Channel: null,
            Candidates: candidates ?? Array.Empty<FlutterSdkCandidate>(),
            HasConflict: pathResult?.HasConflict ?? false,
            Message: "Flutter SDK detection was cancelled.",
            PathDiscovery: pathResult);

    private sealed record VersionMetadata(
        string Version,
        string Channel,
        FlutterVersionMetadataSource Source,
        string Raw);
}
