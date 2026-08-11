using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record TemporaryWorkspaceRequest(
    string Identity,
    string ApprovedTempRoot,
    string WorkspacePath,
    DateTimeOffset CreatedAt,
    TimeSpan Ttl);

public sealed record TemporaryWorkspaceDecision(
    string Identity,
    string ApprovedTempRoot,
    string WorkspacePath,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    TimeSpan Ttl,
    bool Expired,
    bool CleanupAllowed,
    string ReasonCode,
    string Fingerprint);

public static partial class TemporaryWorkspacePolicy
{
    public static readonly TimeSpan MinTtl = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxTtl = TimeSpan.FromHours(24);

    public static TemporaryWorkspaceDecision Evaluate(TemporaryWorkspaceRequest request, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        var identity = NormalizeIdentity(request.Identity);
        var root = NormalizeRoot(request.ApprovedTempRoot);
        var workspace = Path.GetFullPath(request.WorkspacePath);
        if (!IsWithinRoot(root, workspace))
        {
            throw new ArgumentException("Temporary workspace is outside the approved temp root.", nameof(request));
        }
        if (string.Equals(root.TrimEnd(Path.DirectorySeparatorChar), workspace.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Temporary workspace must not equal the approved temp root.", nameof(request));
        }

        var ttlSeconds = Math.Clamp(request.Ttl.TotalSeconds, MinTtl.TotalSeconds, MaxTtl.TotalSeconds);
        var ttl = TimeSpan.FromSeconds(ttlSeconds);
        var createdAt = request.CreatedAt.ToUniversalTime();
        var expiresAt = createdAt + ttl;
        var expired = observedAt.ToUniversalTime() >= expiresAt;
        var reason = expired ? "workspace-expired" : "workspace-active";
        var canonical = string.Join('|', identity, root, workspace, createdAt.ToString("O"), expiresAt.ToString("O"), reason);
        return new TemporaryWorkspaceDecision(identity, root, workspace, createdAt, expiresAt, ttl, expired, expired, reason, Hash(canonical));
    }

    public static string NormalizeIdentity(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (!IdentityRegex().IsMatch(normalized)) throw new ArgumentException("Temporary workspace identity is invalid.", nameof(value));
        return normalized;
    }

    public static string NormalizeRoot(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!Path.IsPathRooted(value)) throw new ArgumentException("Temporary root must be absolute.", nameof(value));
        return Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    public static bool IsWithinRoot(string approvedRoot, string candidatePath)
    {
        var root = NormalizeRoot(approvedRoot);
        var candidate = Path.GetFullPath(candidatePath);
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentityRegex();
}
