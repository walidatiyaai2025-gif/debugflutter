using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record GitRemoteRequest(
    string Identity,
    Uri RemoteUri,
    IReadOnlyCollection<string>? ApprovedHosts = null);

public sealed record GitRemoteDecision(
    bool Allowed,
    string Identity,
    string Scheme,
    string Host,
    string RepositoryPath,
    string SafeDisplayUri,
    string ReasonCode,
    string Fingerprint);

public static partial class GitRemoteSafetyPolicy
{
    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentityPattern();

    public static GitRemoteDecision Evaluate(GitRemoteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.RemoteUri);

        var identity = NormalizeIdentity(request.Identity);
        if (!request.RemoteUri.IsAbsoluteUri)
        {
            throw new ArgumentException("Remote URI must be absolute.", nameof(request));
        }

        var scheme = request.RemoteUri.Scheme.ToLowerInvariant();
        if (scheme is not ("https" or "ssh"))
        {
            throw new ArgumentException("Remote transport must be HTTPS or SSH.", nameof(request));
        }

        if (!string.IsNullOrEmpty(request.RemoteUri.UserInfo))
        {
            throw new ArgumentException("Remote URI credentials are not allowed.", nameof(request));
        }

        var host = request.RemoteUri.IdnHost.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(host) || host.Any(char.IsControl))
        {
            throw new ArgumentException("Remote host is invalid.", nameof(request));
        }

        if (request.ApprovedHosts is { Count: > 0 })
        {
            var approved = request.ApprovedHosts
                .Select(value => value.Trim().ToLowerInvariant())
                .Where(value => value.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!approved.Contains(host))
            {
                return Decision(false, identity, scheme, host, string.Empty, string.Empty, "remote-host-not-approved");
            }
        }

        var repositoryPath = NormalizeRepositoryPath(request.RemoteUri.AbsolutePath);
        var safeDisplay = $"{scheme}://{host}/{repositoryPath}";
        return Decision(true, identity, scheme, host, repositoryPath, safeDisplay, "remote-approved");
    }

    public static string NormalizeIdentity(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (!IdentityPattern().IsMatch(normalized))
        {
            throw new ArgumentException("Remote identity is invalid.", nameof(value));
        }
        return normalized;
    }

    public static string NormalizeRepositoryPath(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().Replace('\\', '/').Trim('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length is < 1 or > 16 || segments.Any(segment => segment is "." or ".." || segment.Any(char.IsControl)))
        {
            throw new ArgumentException("Repository path is invalid.", nameof(value));
        }
        return string.Join('/', segments);
    }

    private static GitRemoteDecision Decision(bool allowed, string identity, string scheme, string host, string path, string display, string reason)
    {
        var canonical = $"{identity}|{scheme}|{host}|{path}|{reason}";
        return new GitRemoteDecision(allowed, identity, scheme, host, path, display, reason, Hash(canonical));
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
