using System.Security.Cryptography;
using System.Text;

namespace FlutterBuildDoctor.Application.Artifacts;

public enum PublicationArtifactKind
{
    Apk = 0,
    Aab = 1
}

public enum PublicationBuildMode
{
    Debug = 0,
    Profile = 1,
    Release = 2
}

public sealed record ArtifactPublicationRequest(
    string WorkspaceRoot,
    string ArtifactPath,
    PublicationArtifactKind Kind,
    PublicationBuildMode Mode,
    string Channel,
    bool IsVerified,
    int RetentionDays = ArtifactPublicationPolicy.DefaultRetentionDays);

public sealed record ArtifactPublicationDecision(
    bool Allowed,
    string Channel,
    string PublicationName,
    int RetentionDays,
    string PublicationKey,
    string ReasonCode);

public static class ArtifactPublicationPolicy
{
    public const int DefaultRetentionDays = 14;
    public const int MinRetentionDays = 1;
    public const int MaxRetentionDays = 90;

    public static ArtifactPublicationDecision Evaluate(ArtifactPublicationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var root = NormalizeRoot(request.WorkspaceRoot);
        var artifactPath = NormalizeArtifactPath(root, request.ArtifactPath);
        var channel = NormalizeChannel(request.Channel);
        var retentionDays = Math.Clamp(request.RetentionDays, MinRetentionDays, MaxRetentionDays);
        var publicationName = BuildPublicationName(artifactPath, request.Kind, request.Mode);

        if (!request.IsVerified)
        {
            return BuildDecision(false, channel, publicationName, retentionDays, root, artifactPath, request, "artifact-unverified");
        }

        if (request.Kind == PublicationArtifactKind.Aab && request.Mode != PublicationBuildMode.Release)
        {
            return BuildDecision(false, channel, publicationName, retentionDays, root, artifactPath, request, "aab-requires-release");
        }

        if (request.Kind == PublicationArtifactKind.Apk
            && request.Mode == PublicationBuildMode.Debug
            && !channel.Equals("local", StringComparison.Ordinal))
        {
            return BuildDecision(false, channel, publicationName, retentionDays, root, artifactPath, request, "debug-apk-local-only");
        }

        return BuildDecision(true, channel, publicationName, retentionDays, root, artifactPath, request, "publish-ready");
    }

    public static string NormalizeChannel(string channel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        return channel.Trim().ToLowerInvariant() switch
        {
            "local" => "local",
            "github" or "github-actions" => "github",
            "internal" => "internal",
            _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unsupported publication channel.")
        };
    }

    public static string NormalizeArtifactPath(string workspaceRoot, string artifactPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);
        var root = NormalizeRoot(workspaceRoot);
        var fullPath = Path.GetFullPath(artifactPath, root);
        var relative = Path.GetRelativePath(root, fullPath);
        if (relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
        {
            throw new InvalidOperationException("Artifact path escapes the workspace root.");
        }

        return fullPath;
    }

    private static string NormalizeRoot(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot.Trim()));
    }

    private static string BuildPublicationName(string artifactPath, PublicationArtifactKind kind, PublicationBuildMode mode)
    {
        var fileName = Path.GetFileNameWithoutExtension(artifactPath).Trim().ToLowerInvariant();
        if (fileName.Length == 0)
        {
            throw new ArgumentException("Artifact file name cannot be empty.", nameof(artifactPath));
        }

        var extension = kind == PublicationArtifactKind.Aab ? ".aab" : ".apk";
        return $"{fileName}-{mode.ToString().ToLowerInvariant()}{extension}";
    }

    private static ArtifactPublicationDecision BuildDecision(
        bool allowed,
        string channel,
        string publicationName,
        int retentionDays,
        string root,
        string artifactPath,
        ArtifactPublicationRequest request,
        string reasonCode)
    {
        var canonical = string.Join('|',
            root,
            artifactPath,
            request.Kind,
            request.Mode,
            channel,
            request.IsVerified,
            retentionDays,
            publicationName,
            reasonCode);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return new ArtifactPublicationDecision(allowed, channel, publicationName, retentionDays, key, reasonCode);
    }
}
