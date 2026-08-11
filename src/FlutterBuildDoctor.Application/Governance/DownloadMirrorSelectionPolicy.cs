using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record DownloadMirrorCandidate(
    string Identity,
    Uri Endpoint,
    int Priority,
    bool IsHealthy,
    bool IsTrusted,
    TimeSpan Latency);

public sealed record NormalizedDownloadMirror(
    string Identity,
    Uri Endpoint,
    int Priority,
    bool IsTrusted,
    TimeSpan Latency);

public sealed record DownloadMirrorSelectionDecision(
    bool Available,
    NormalizedDownloadMirror? Selected,
    IReadOnlyList<NormalizedDownloadMirror> Candidates,
    string ReasonCode,
    string Fingerprint);

public static partial class DownloadMirrorSelectionPolicy
{
    public const int MaxMirrors = 64;
    public const int MaxPriority = 100;
    public static readonly TimeSpan MaxLatency = TimeSpan.FromMinutes(1);

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentityPattern();

    public static DownloadMirrorSelectionDecision Evaluate(IEnumerable<DownloadMirrorCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var input = candidates.ToArray();
        if (input.Length > MaxMirrors)
        {
            throw new ArgumentOutOfRangeException(nameof(candidates), "Mirror count exceeds the supported bound.");
        }

        var normalized = input.Select(Normalize).ToArray();
        var duplicate = normalized.GroupBy(item => item.Mirror.Identity, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException("Mirror identities must be unique.", nameof(candidates));
        }

        var ordered = normalized
            .Where(item => item.IsHealthy)
            .Select(item => item.Mirror)
            .OrderByDescending(item => item.IsTrusted)
            .ThenBy(item => item.Priority)
            .ThenBy(item => item.Latency)
            .ThenBy(item => item.Identity, StringComparer.Ordinal)
            .ToArray();

        var selected = ordered.FirstOrDefault();
        var available = selected is not null;
        var reason = available ? "mirror-selected" : "no-healthy-mirror";
        var canonical = string.Join('\n', ordered.Select(item => $"{item.Identity}|{item.Endpoint.AbsoluteUri}|{item.Priority}|{item.IsTrusted}|{item.Latency.Ticks}"));
        return new DownloadMirrorSelectionDecision(available, selected, ordered, reason, Hash($"{reason}|{canonical}"));
    }

    private static NormalizedMirrorState Normalize(DownloadMirrorCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var identity = candidate.Identity.Trim().ToLowerInvariant();
        if (!IdentityPattern().IsMatch(identity))
        {
            throw new ArgumentException("Mirror identity is invalid.", nameof(candidate));
        }
        if (!candidate.Endpoint.IsAbsoluteUri || !string.Equals(candidate.Endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Mirror endpoint must use HTTPS.", nameof(candidate));
        }
        if (!string.IsNullOrEmpty(candidate.Endpoint.UserInfo))
        {
            throw new ArgumentException("Mirror endpoint credentials are not allowed.", nameof(candidate));
        }
        if (candidate.Latency < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(candidate), "Mirror latency cannot be negative.");
        }

        var priority = Math.Clamp(candidate.Priority, 0, MaxPriority);
        var latency = candidate.Latency > MaxLatency ? MaxLatency : candidate.Latency;
        var endpoint = new UriBuilder(candidate.Endpoint) { Host = candidate.Endpoint.IdnHost.ToLowerInvariant() }.Uri;
        return new NormalizedMirrorState(
            new NormalizedDownloadMirror(identity, endpoint, priority, candidate.IsTrusted, latency),
            candidate.IsHealthy);
    }

    private sealed record NormalizedMirrorState(NormalizedDownloadMirror Mirror, bool IsHealthy);

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
