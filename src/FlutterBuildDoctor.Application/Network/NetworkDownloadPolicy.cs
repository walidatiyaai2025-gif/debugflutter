using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Network;

public sealed record NetworkDownloadRequest(
    Uri Source,
    string DestinationFileName,
    TimeSpan Timeout,
    long MaxBytes,
    Uri? RedirectTarget = null,
    IReadOnlyCollection<string>? ApprovedHosts = null);

public sealed record NetworkDownloadDecision(
    bool Allowed,
    Uri Source,
    Uri? RedirectTarget,
    string DestinationFileName,
    TimeSpan Timeout,
    long MaxBytes,
    string ReasonCode,
    string Fingerprint);

public static partial class NetworkDownloadPolicy
{
    public static readonly TimeSpan MinTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan MaxTimeout = TimeSpan.FromMinutes(30);
    public const long MinMaxBytes = 1_024;
    public const long MaxMaxBytes = 4L * 1024 * 1024 * 1024;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".apk", ".aab", ".json", ".txt", ".sha256"
    };

    public static NetworkDownloadDecision Evaluate(NetworkDownloadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateUri(request.Source, nameof(request.Source));
        if (request.RedirectTarget is not null)
        {
            ValidateUri(request.RedirectTarget, nameof(request.RedirectTarget));
        }

        var approvedHosts = NormalizeApprovedHosts(request.ApprovedHosts);
        var effectiveUri = request.RedirectTarget ?? request.Source;
        var hostAllowed = approvedHosts.Length == 0 || approvedHosts.Contains(effectiveUri.IdnHost, StringComparer.OrdinalIgnoreCase);
        var fileName = NormalizeDestinationFileName(request.DestinationFileName);
        var timeout = TimeSpan.FromSeconds(Math.Clamp(request.Timeout.TotalSeconds, MinTimeout.TotalSeconds, MaxTimeout.TotalSeconds));
        var maxBytes = Math.Clamp(request.MaxBytes, MinMaxBytes, MaxMaxBytes);
        var allowed = hostAllowed;
        var reason = hostAllowed ? "download-approved" : "host-not-approved";

        var canonical = string.Join('|',
            request.Source.AbsoluteUri,
            request.RedirectTarget?.AbsoluteUri ?? string.Empty,
            fileName,
            timeout.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            maxBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            string.Join(',', approvedHosts),
            reason);

        return new NetworkDownloadDecision(allowed, request.Source, request.RedirectTarget, fileName, timeout, maxBytes, reason, Hash(canonical));
    }

    public static void ValidateUri(Uri uri, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Download URL must use HTTPS.", parameterName);
        }
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException("Download URL must not contain embedded credentials.", parameterName);
        }
    }

    public static string NormalizeDestinationFileName(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var trimmed = fileName.Trim();
        if (!string.Equals(Path.GetFileName(trimmed), trimmed, StringComparison.Ordinal)
            || trimmed.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Destination file name must not contain path traversal.", nameof(fileName));
        }

        var normalized = InvalidFileNameCharsRegex().Replace(trimmed.ToLowerInvariant(), "-").Trim('-', '.');
        if (normalized.Length == 0 || !AllowedExtensions.Contains(Path.GetExtension(normalized)))
        {
            throw new ArgumentException("Destination file extension is not approved.", nameof(fileName));
        }
        return normalized;
    }

    private static string[] NormalizeApprovedHosts(IReadOnlyCollection<string>? approvedHosts)
        => approvedHosts is null
            ? Array.Empty<string>()
            : approvedHosts
                .Where(host => !string.IsNullOrWhiteSpace(host))
                .Select(host => host.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(host => host, StringComparer.Ordinal)
                .ToArray();

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [GeneratedRegex("[^a-z0-9._-]+", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidFileNameCharsRegex();
}
