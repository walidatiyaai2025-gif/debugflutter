using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Repositories;

public sealed record RepositoryIntakeRequest(
    string RepositoryUrl,
    string? Branch = null,
    int? CloneDepth = null,
    string? CommitSha = null);

public sealed record RepositoryIntakeDecision(
    string NormalizedRepositoryUrl,
    string Branch,
    int CloneDepth,
    bool IsDetachedRef,
    string? CommitSha,
    string WorkspaceSlug,
    string Fingerprint);

public static partial class RepositoryIntakePolicy
{
    public const int DefaultCloneDepth = 50;
    public const int MaxCloneDepth = 1000;

    public static RepositoryIntakeDecision Prepare(RepositoryIntakeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var repositoryUrl = NormalizeRepositoryUrl(request.RepositoryUrl);
        var branch = NormalizeBranch(request.Branch);
        var cloneDepth = Math.Clamp(request.CloneDepth ?? DefaultCloneDepth, 1, MaxCloneDepth);
        var commitSha = NormalizeCommitSha(request.CommitSha);
        var detached = commitSha is not null;
        var workspaceSlug = BuildWorkspaceSlug(repositoryUrl);
        var fingerprint = ComputeFingerprint(repositoryUrl, branch, cloneDepth, commitSha);

        return new RepositoryIntakeDecision(
            repositoryUrl,
            branch,
            cloneDepth,
            detached,
            commitSha,
            workspaceSlug,
            fingerprint);
    }

    public static string NormalizeRepositoryUrl(string repositoryUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryUrl);
        var value = repositoryUrl.Trim();

        if (value.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
        {
            var path = value["git@github.com:".Length..];
            return NormalizeGitHubPath(path);
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only HTTPS or SSH GitHub repository URLs are supported.", nameof(repositoryUrl));
        }

        return NormalizeGitHubPath(uri.AbsolutePath);
    }

    public static string NormalizeBranch(string? branch)
    {
        var value = string.IsNullOrWhiteSpace(branch) ? "main" : branch.Trim();
        if (value.Contains("..", StringComparison.Ordinal)
            || value.Contains('\\')
            || value.Contains("@{", StringComparison.Ordinal)
            || value.StartsWith('/')
            || value.EndsWith('/'))
        {
            throw new ArgumentException("Branch contains unsafe traversal or ref syntax.", nameof(branch));
        }

        return value;
    }

    public static string? NormalizeCommitSha(string? commitSha)
    {
        if (string.IsNullOrWhiteSpace(commitSha))
        {
            return null;
        }

        var value = commitSha.Trim().ToLowerInvariant();
        if (!CommitShaRegex().IsMatch(value))
        {
            throw new ArgumentException("Commit SHA must contain 7 to 40 hexadecimal characters.", nameof(commitSha));
        }

        return value;
    }

    public static string BuildWorkspaceSlug(string normalizedRepositoryUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedRepositoryUrl);
        var uri = new Uri(normalizedRepositoryUrl, UriKind.Absolute);
        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2)
        {
            throw new ArgumentException("Repository URL must contain owner and repository segments.", nameof(normalizedRepositoryUrl));
        }

        return $"{SanitizeSlugToken(segments[0])}-{SanitizeSlugToken(segments[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? segments[1][..^4] : segments[1])}";
    }

    private static string NormalizeGitHubPath(string path)
    {
        var segments = path.Trim().Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2)
        {
            throw new ArgumentException("GitHub repository URL must contain exactly owner and repository segments.", nameof(path));
        }

        var owner = segments[0].Trim();
        var repository = segments[1].Trim();
        if (repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            repository = repository[..^4];
        }

        if (!RepositoryTokenRegex().IsMatch(owner) || !RepositoryTokenRegex().IsMatch(repository))
        {
            throw new ArgumentException("GitHub owner or repository contains unsupported characters.", nameof(path));
        }

        return $"https://github.com/{owner}/{repository}.git";
    }

    private static string SanitizeSlugToken(string value)
    {
        var normalized = SlugInvalidRegex().Replace(value.Trim().ToLowerInvariant(), "-").Trim('-');
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Workspace slug token cannot be empty.", nameof(value));
        }

        return normalized;
    }

    private static string ComputeFingerprint(string repositoryUrl, string branch, int cloneDepth, string? commitSha)
    {
        var canonical = string.Join('|', repositoryUrl, branch, cloneDepth.ToString(System.Globalization.CultureInfo.InvariantCulture), commitSha ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    [GeneratedRegex("^[a-f0-9]{7,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitShaRegex();

    [GeneratedRegex("^[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryTokenRegex();

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex SlugInvalidRegex();
}
