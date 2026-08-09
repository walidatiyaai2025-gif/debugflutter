using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Git.Validation;

public sealed partial class GitRepositoryUrlValidator : IGitRepositoryUrlValidator
{
    public GitRepositoryUrlValidationResult Validate(string? repositoryUrl)
    {
        if (string.IsNullOrWhiteSpace(repositoryUrl))
        {
            return GitRepositoryUrlValidationResult.Invalid(
                GitRepositoryUrlError.Empty,
                "Enter a Git repository URL.");
        }

        var candidate = repositoryUrl.Trim();

        if (candidate.Any(char.IsControl))
        {
            return GitRepositoryUrlValidationResult.Invalid(
                GitRepositoryUrlError.Malformed,
                "Repository URL contains invalid control characters.");
        }

        if (LooksLikeLocalPath(candidate))
        {
            return GitRepositoryUrlValidationResult.Invalid(
                GitRepositoryUrlError.LocalPathNotAllowed,
                "A remote Git repository URL is required; local file paths are not supported here.");
        }

        var scpMatch = ScpLikeRemoteRegex().Match(candidate);
        if (scpMatch.Success)
        {
            var path = scpMatch.Groups["path"].Value;
            if (!HasRepositoryPath(path))
            {
                return GitRepositoryUrlValidationResult.Invalid(
                    GitRepositoryUrlError.MissingRepositoryPath,
                    "Repository URL must include a repository path.");
            }

            if (ContainsQueryOrFragment(path))
            {
                return GitRepositoryUrlValidationResult.Invalid(
                    GitRepositoryUrlError.QueryOrFragmentNotAllowed,
                    "Repository URL must not include a query string or fragment.");
            }

            return GitRepositoryUrlValidationResult.Valid(candidate, GitRepositoryTransport.Ssh);
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            return GitRepositoryUrlValidationResult.Invalid(
                GitRepositoryUrlError.Malformed,
                "Repository URL is not a valid absolute Git remote URL.");
        }

        var transport = GetTransport(uri.Scheme);
        if (transport == GitRepositoryTransport.Unknown)
        {
            return GitRepositoryUrlValidationResult.Invalid(
                GitRepositoryUrlError.UnsupportedScheme,
                $"Unsupported Git URL scheme '{uri.Scheme}'. Use HTTPS, HTTP, SSH, or git://.");
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            return GitRepositoryUrlValidationResult.Invalid(
                GitRepositoryUrlError.MissingHost,
                "Repository URL must include a host name.");
        }

        if (!HasRepositoryPath(uri.AbsolutePath))
        {
            return GitRepositoryUrlValidationResult.Invalid(
                GitRepositoryUrlError.MissingRepositoryPath,
                "Repository URL must include a repository path.");
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return GitRepositoryUrlValidationResult.Invalid(
                GitRepositoryUrlError.QueryOrFragmentNotAllowed,
                "Repository URL must not include a query string or fragment.");
        }

        if (transport is GitRepositoryTransport.Http or GitRepositoryTransport.Https &&
            !string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            return GitRepositoryUrlValidationResult.Invalid(
                GitRepositoryUrlError.CredentialsNotAllowed,
                "Do not embed credentials or access tokens in an HTTP(S) repository URL.");
        }

        if (transport == GitRepositoryTransport.Ssh && uri.UserInfo.Contains(':'))
        {
            return GitRepositoryUrlValidationResult.Invalid(
                GitRepositoryUrlError.CredentialsNotAllowed,
                "Do not embed an SSH password in the repository URL.");
        }

        return GitRepositoryUrlValidationResult.Valid(candidate.TrimEnd('/'), transport);
    }

    private static GitRepositoryTransport GetTransport(string scheme)
        => scheme.ToLowerInvariant() switch
        {
            "https" => GitRepositoryTransport.Https,
            "http" => GitRepositoryTransport.Http,
            "ssh" => GitRepositoryTransport.Ssh,
            "git" => GitRepositoryTransport.Git,
            _ => GitRepositoryTransport.Unknown
        };

    private static bool HasRepositoryPath(string path)
    {
        var trimmed = path.Trim().Trim('/');
        return !string.IsNullOrWhiteSpace(trimmed) && trimmed is not "." and not "..";
    }

    private static bool ContainsQueryOrFragment(string value)
        => value.Contains('?') || value.Contains('#');

    private static bool LooksLikeLocalPath(string value)
        => WindowsDrivePathRegex().IsMatch(value) ||
           value.StartsWith("\\\\", StringComparison.Ordinal) ||
           value.StartsWith("./", StringComparison.Ordinal) ||
           value.StartsWith("../", StringComparison.Ordinal) ||
           value.StartsWith("/", StringComparison.Ordinal);

    [GeneratedRegex("^[A-Za-z]:[\\\\/]", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsDrivePathRegex();

    [GeneratedRegex(
        "^(?<user>[A-Za-z0-9._-]+)@(?<host>[A-Za-z0-9.-]+):(?<path>[^\\s]+)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ScpLikeRemoteRegex();
}
