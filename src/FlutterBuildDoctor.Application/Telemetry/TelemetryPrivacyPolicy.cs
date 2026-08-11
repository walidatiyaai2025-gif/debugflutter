using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Telemetry;

public sealed record TelemetryRequest(
    string EventName,
    string RepositoryIdentity,
    bool OptedIn = false,
    IReadOnlyDictionary<string, string?>? Properties = null);

public sealed record TelemetryPayload(
    bool Enabled,
    string EventName,
    string? RepositoryHash,
    IReadOnlyDictionary<string, string> Properties,
    string Fingerprint,
    string ReasonCode);

public static partial class TelemetryPrivacyPolicy
{
    public const int MaxProperties = 32;
    public const int MaxValueLength = 200;

    private static readonly string[] SecretTokens =
    [
        "password",
        "passwd",
        "token",
        "secret",
        "api_key",
        "apikey",
        "authorization"
    ];

    public static TelemetryPayload Prepare(TelemetryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var eventName = NormalizeEventName(request.EventName);

        if (!request.OptedIn)
        {
            return BuildPayload(false, eventName, null, new SortedDictionary<string, string>(StringComparer.Ordinal), "telemetry-disabled");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(request.RepositoryIdentity);
        var repositoryHash = HashRepositoryIdentity(request.RepositoryIdentity);
        var properties = NormalizeProperties(request.Properties);
        return BuildPayload(true, eventName, repositoryHash, properties, "telemetry-ready");
    }

    public static string NormalizeEventName(string eventName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        var normalized = EventInvalidRegex().Replace(eventName.Trim().ToLowerInvariant(), "-").Trim('-');
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Telemetry event name cannot be empty after normalization.", nameof(eventName));
        }

        return normalized.Length <= 80 ? normalized : normalized[..80].TrimEnd('-');
    }

    public static string HashRepositoryIdentity(string repositoryIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryIdentity);
        var normalized = repositoryIdentity.Trim().ToLowerInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    private static IReadOnlyDictionary<string, string> NormalizeProperties(IReadOnlyDictionary<string, string?>? properties)
    {
        if (properties is null || properties.Count == 0)
        {
            return new SortedDictionary<string, string>(StringComparer.Ordinal);
        }

        if (properties.Count > MaxProperties)
        {
            throw new ArgumentOutOfRangeException(nameof(properties), $"Telemetry properties cannot exceed {MaxProperties} entries.");
        }

        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in properties)
        {
            var key = NormalizePropertyKey(pair.Key);
            if (SecretTokens.Any(token => key.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Telemetry field '{key}' may contain secret material.");
            }

            if (string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }

            var value = pair.Value.Trim();
            if (value.Length > MaxValueLength)
            {
                value = value[..MaxValueLength];
            }

            result[key] = value;
        }

        return result;
    }

    private static string NormalizePropertyKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var normalized = EventInvalidRegex().Replace(key.Trim().ToLowerInvariant(), "_").Trim('_');
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Telemetry property key cannot be empty after normalization.", nameof(key));
        }

        return normalized;
    }

    private static TelemetryPayload BuildPayload(
        bool enabled,
        string eventName,
        string? repositoryHash,
        IReadOnlyDictionary<string, string> properties,
        string reasonCode)
    {
        var canonicalProperties = properties.Select(pair => $"{pair.Key}={pair.Value}");
        var canonical = string.Join('|', canonicalProperties.Prepend($"{enabled}:{eventName}:{repositoryHash ?? string.Empty}:{reasonCode}"));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return new TelemetryPayload(enabled, eventName, repositoryHash, properties, fingerprint, reasonCode);
    }

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex EventInvalidRegex();
}
