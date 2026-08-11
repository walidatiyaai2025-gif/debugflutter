using System.Security.Cryptography;
using System.Text;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record ToolCapabilityCandidate(string Identity, string Version, IReadOnlyCollection<string> Capabilities);

public sealed record ToolCapabilityMatch(
    string Identity,
    string Version,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> MissingCapabilities,
    bool FullyCapable);

public sealed record ToolCapabilityResolution(
    IReadOnlyList<ToolCapabilityMatch> Matches,
    string ReasonCode,
    string Fingerprint);

public static class ToolCapabilityMatchingPolicy
{
    public const int MaxCapabilities = 64;
    public const int MaxTools = 64;

    public static ToolCapabilityResolution Match(
        IEnumerable<ToolCapabilityCandidate> candidates,
        IEnumerable<string> requiredCapabilities)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(requiredCapabilities);
        var tools = candidates.ToArray();
        if (tools.Length > MaxTools)
            throw new ArgumentOutOfRangeException(nameof(candidates));

        var required = NormalizeCapabilities(requiredCapabilities);
        var duplicateTool = tools.GroupBy(item => NormalizeIdentity(item.Identity), StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateTool is not null)
            throw new ArgumentException("Duplicate tool identity.", nameof(candidates));

        var matches = tools.Select(item =>
            {
                var identity = NormalizeIdentity(item.Identity);
                var capabilities = NormalizeCapabilities(item.Capabilities);
                var missing = required.Except(capabilities, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
                return new ToolCapabilityMatch(identity, item.Version.Trim(), capabilities, missing, missing.Length == 0);
            })
            .OrderByDescending(item => item.FullyCapable)
            .ThenBy(item => item.MissingCapabilities.Count)
            .ThenBy(item => item.Identity, StringComparer.Ordinal)
            .ToArray();

        var payload = string.Join("\n", matches.Select(item =>
            $"{item.Identity}|{item.Version}|{string.Join(',', item.Capabilities)}|{string.Join(',', item.MissingCapabilities)}"));
        var reason = matches.Any(item => item.FullyCapable) ? "capable-tool-found" : "capabilities-missing";
        return new ToolCapabilityResolution(matches, reason, Hash(payload));
    }

    public static string[] NormalizeCapabilities(IEnumerable<string> values)
    {
        var normalized = values.Select(value =>
            {
                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException("Capability is required.", nameof(values));
                var item = value.Trim().ToLowerInvariant();
                if (item.Length > 64 || item.Any(char.IsControl))
                    throw new ArgumentException("Capability is invalid.", nameof(values));
                return item;
            })
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (normalized.Length > MaxCapabilities)
            throw new ArgumentOutOfRangeException(nameof(values));
        return normalized;
    }

    private static string NormalizeIdentity(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Tool identity is required.", nameof(value));
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > 128 || normalized.Any(char.IsControl))
            throw new ArgumentException("Tool identity is invalid.", nameof(value));
        return normalized;
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
