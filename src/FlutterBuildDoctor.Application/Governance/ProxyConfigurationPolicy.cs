using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record ProxyConfigurationRequest(Uri Proxy, IReadOnlyCollection<string>? BypassHosts = null);

public sealed record ProxyConfigurationDecision(
    Uri Proxy,
    IReadOnlyList<string> BypassHosts,
    string SafeDisplayText,
    string ReasonCode,
    string Fingerprint);

public static partial class ProxyConfigurationPolicy
{
    public const int MaxBypassHosts = 256;

    public static ProxyConfigurationDecision Evaluate(ProxyConfigurationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateProxy(request.Proxy);
        var bypass = NormalizeBypassHosts(request.BypassHosts);
        var host = request.Proxy.IdnHost.ToLowerInvariant();
        var port = request.Proxy.Port;
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Proxy port must be between 1 and 65535.");
        }

        var builder = new UriBuilder(request.Proxy.Scheme.ToLowerInvariant(), host, port);
        var normalizedProxy = builder.Uri;
        var safeDisplay = $"{normalizedProxy.Scheme}://{host}:{port}";
        var canonical = safeDisplay + "|" + string.Join(',', bypass);
        return new ProxyConfigurationDecision(normalizedProxy, bypass, safeDisplay, "proxy-valid", Hash(canonical));
    }

    public static void ValidateProxy(Uri proxy)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        if (!proxy.IsAbsoluteUri || (proxy.Scheme != Uri.UriSchemeHttp && proxy.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Proxy URI must use HTTP or HTTPS.", nameof(proxy));
        }
        if (proxy.OriginalString.Any(char.IsControl))
        {
            throw new ArgumentException("Proxy URI contains control characters.", nameof(proxy));
        }
        if (!string.IsNullOrEmpty(proxy.UserInfo))
        {
            throw new ArgumentException("Proxy credentials must not be embedded in diagnostic configuration.", nameof(proxy));
        }
        if (string.IsNullOrWhiteSpace(proxy.IdnHost))
        {
            throw new ArgumentException("Proxy host is required.", nameof(proxy));
        }
    }

    public static string[] NormalizeBypassHosts(IReadOnlyCollection<string>? bypassHosts)
    {
        if (bypassHosts is null) return Array.Empty<string>();
        if (bypassHosts.Count > MaxBypassHosts)
        {
            throw new ArgumentOutOfRangeException(nameof(bypassHosts), "Proxy bypass host count exceeds the supported bound.");
        }

        return bypassHosts.Select(NormalizeHost)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    public static string NormalizeHost(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Any(char.IsControl)) throw new ArgumentException("Bypass host contains control characters.", nameof(value));
        var normalized = value.Trim().TrimEnd('.').ToLowerInvariant();
        if (!HostRegex().IsMatch(normalized)) throw new ArgumentException("Bypass host is invalid.", nameof(value));
        return normalized;
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [GeneratedRegex("^(?:\\*\\.)?[a-z0-9](?:[a-z0-9.-]{0,251}[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex HostRegex();
}
