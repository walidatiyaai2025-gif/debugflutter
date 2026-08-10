using System.Reflection;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.App.Services;

public sealed partial class ApplicationIdentityService : IApplicationIdentityService
{
    private readonly Func<string, string?> _environmentReader;

    public ApplicationIdentityService()
        : this(typeof(App).Assembly, System.Environment.GetEnvironmentVariable)
    {
    }

    public ApplicationIdentityService(
        Assembly assembly,
        Func<string, string?> environmentReader)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        _environmentReader = environmentReader ?? throw new ArgumentNullException(nameof(environmentReader));
        Current = BuildIdentity(assembly);
    }

    public ApplicationIdentity Current { get; }

    private ApplicationIdentity BuildIdentity(Assembly assembly)
    {
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        var productVersion = NormalizeProductVersion(informationalVersion)
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";

        var commit = FindCommitSha(assembly, informationalVersion);
        var buildNumber = FindBuildNumber(assembly);

        return new ApplicationIdentity(productVersion, buildNumber, commit);
    }

    private string FindBuildNumber(Assembly assembly)
    {
        var githubRunNumber = _environmentReader("GITHUB_RUN_NUMBER");
        if (IsDigitsOnly(githubRunNumber))
        {
            return githubRunNumber!;
        }

        var buildBuildId = _environmentReader("BUILD_BUILDID");
        if (IsDigitsOnly(buildBuildId))
        {
            return buildBuildId!;
        }

        return assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version
            ?? assembly.GetName().Version?.ToString()
            ?? "local";
    }

    private string? FindCommitSha(Assembly assembly, string? informationalVersion)
    {
        var metadataCommit = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute =>
                string.Equals(attribute.Key, "RepositoryCommit", StringComparison.OrdinalIgnoreCase))
            ?.Value;

        var candidates = new[]
        {
            metadataCommit,
            ExtractInformationalVersionCommit(informationalVersion),
            _environmentReader("GITHUB_SHA"),
            _environmentReader("BUILD_SOURCEVERSION")
        };

        return candidates.FirstOrDefault(IsSafeCommitSha);
    }

    private static string? NormalizeProductVersion(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return null;
        }

        var separatorIndex = informationalVersion.IndexOf('+');
        var value = separatorIndex >= 0
            ? informationalVersion[..separatorIndex]
            : informationalVersion;

        value = value.Trim();
        return value.Length is > 0 and <= 64 ? value : null;
    }

    private static string? ExtractInformationalVersionCommit(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return null;
        }

        var separatorIndex = informationalVersion.IndexOf('+');
        if (separatorIndex < 0 || separatorIndex == informationalVersion.Length - 1)
        {
            return null;
        }

        var suffix = informationalVersion[(separatorIndex + 1)..].Trim();
        return IsSafeCommitSha(suffix) ? suffix : null;
    }

    private static bool IsDigitsOnly(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 32
        && value.All(char.IsDigit);

    private static bool IsSafeCommitSha(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && CommitShaRegex().IsMatch(value);

    [GeneratedRegex("^[0-9a-fA-F]{7,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitShaRegex();
}
