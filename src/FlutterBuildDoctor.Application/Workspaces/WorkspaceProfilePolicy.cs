using System.Security.Cryptography;
using System.Text;

namespace FlutterBuildDoctor.Application.Workspaces;

public sealed record WorkspaceProfileDefaults(
    string? PreferredJdk = null,
    string? PreferredDevice = null,
    string PreferredBuildProfile = "debug");

public sealed record WorkspaceProfileOverride(
    string? PreferredJdk = null,
    string? PreferredDevice = null,
    string? PreferredBuildProfile = null);

public sealed record WorkspaceProfileInput(
    string Name,
    string? PreferredJdk,
    string? PreferredDevice,
    string? PreferredBuildProfile,
    DateTimeOffset LastUsedAt);

public sealed record ResolvedWorkspaceProfile(
    string Name,
    string? PreferredJdk,
    string? PreferredDevice,
    string PreferredBuildProfile,
    DateTimeOffset LastUsedAtUtc,
    int CompletenessScore,
    string Fingerprint);

public static class WorkspaceProfilePolicy
{
    private static readonly HashSet<string> JdkTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "embedded",
        "android-studio"
    };

    private static readonly HashSet<string> BuildProfiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "debug",
        "profile",
        "release"
    };

    public static ResolvedWorkspaceProfile Resolve(
        WorkspaceProfileInput input,
        WorkspaceProfileDefaults defaults,
        WorkspaceProfileOverride? repositoryOverride = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(defaults);

        var name = NormalizeName(input.Name);
        var jdk = FirstNonBlank(repositoryOverride?.PreferredJdk, input.PreferredJdk, defaults.PreferredJdk);
        var device = FirstNonBlank(repositoryOverride?.PreferredDevice, input.PreferredDevice, defaults.PreferredDevice);
        var buildProfile = FirstNonBlank(repositoryOverride?.PreferredBuildProfile, input.PreferredBuildProfile, defaults.PreferredBuildProfile)
            ?? "debug";

        jdk = ValidateJdk(jdk);
        device = NormalizeDevice(device);
        buildProfile = NormalizeBuildProfile(buildProfile);
        var lastUsedUtc = input.LastUsedAt.ToUniversalTime();
        var score = Completeness(name, jdk, device, buildProfile);
        var fingerprint = Fingerprint(name, jdk, device, buildProfile, lastUsedUtc);

        return new ResolvedWorkspaceProfile(name, jdk, device, buildProfile, lastUsedUtc, score, fingerprint);
    }

    public static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Workspace profile name is required.", nameof(name));
        }

        var normalized = string.Join(' ', name.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length > 80 || normalized.Any(char.IsControl))
        {
            throw new ArgumentException("Workspace profile name is invalid.", nameof(name));
        }

        return normalized;
    }

    public static string? ValidateJdk(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > 512 || normalized.Any(char.IsControl))
        {
            throw new ArgumentException("Preferred JDK value is invalid.", nameof(value));
        }

        if (JdkTokens.Contains(normalized)) return normalized.ToLowerInvariant();
        if (!Path.IsPathFullyQualified(normalized))
        {
            throw new ArgumentException("Preferred JDK must be a supported token or fully-qualified path.", nameof(value));
        }

        return normalized;
    }

    public static string? NormalizeDevice(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > 128 || normalized.Any(char.IsControl))
        {
            throw new ArgumentException("Preferred device identifier is invalid.", nameof(value));
        }

        return normalized;
    }

    public static string NormalizeBuildProfile(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Build profile is required.", nameof(value));
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (!BuildProfiles.Contains(normalized))
        {
            throw new ArgumentException("Build profile must be debug, profile or release.", nameof(value));
        }

        return normalized;
    }

    private static int Completeness(string name, string? jdk, string? device, string buildProfile)
    {
        var score = string.IsNullOrWhiteSpace(name) ? 0 : 15;
        if (!string.IsNullOrWhiteSpace(jdk)) score += 35;
        if (!string.IsNullOrWhiteSpace(device)) score += 25;
        if (!string.IsNullOrWhiteSpace(buildProfile)) score += 25;
        return Math.Clamp(score, 0, 100);
    }

    private static string? FirstNonBlank(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string Fingerprint(
        string name,
        string? jdk,
        string? device,
        string buildProfile,
        DateTimeOffset lastUsedAtUtc)
    {
        var canonical = $"{name}|{jdk ?? string.Empty}|{device ?? string.Empty}|{buildProfile}|{lastUsedAtUtc:O}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
