namespace FlutterBuildDoctor.Git.Validation;

public enum GitRepositoryTransport
{
    Unknown = 0,
    Https,
    Http,
    Ssh,
    Git
}

public enum GitRepositoryUrlError
{
    None = 0,
    Empty,
    Malformed,
    LocalPathNotAllowed,
    UnsupportedScheme,
    MissingHost,
    MissingRepositoryPath,
    CredentialsNotAllowed,
    QueryOrFragmentNotAllowed
}

public sealed record GitRepositoryUrlValidationResult(
    bool IsValid,
    string? NormalizedUrl,
    GitRepositoryTransport Transport,
    GitRepositoryUrlError Error,
    string Message)
{
    public static GitRepositoryUrlValidationResult Valid(
        string normalizedUrl,
        GitRepositoryTransport transport)
        => new(true, normalizedUrl, transport, GitRepositoryUrlError.None, "Repository URL is valid.");

    public static GitRepositoryUrlValidationResult Invalid(
        GitRepositoryUrlError error,
        string message)
        => new(false, null, GitRepositoryTransport.Unknown, error, message);
}

public interface IGitRepositoryUrlValidator
{
    GitRepositoryUrlValidationResult Validate(string? repositoryUrl);
}
